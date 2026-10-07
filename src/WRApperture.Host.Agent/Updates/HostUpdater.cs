using WRApperture.Host.Core.Agent;
using WRApperture.Host.Core.Platform;
using WRApperture.Shared;
using WRApperture.Shared.Updates;

namespace WRApperture.Host.Agent.Updates;

internal sealed class HostUpdaterOptions
{
    public Version CurrentVersion { get; init; } = ProductVersion.Current;
    public TimeSpan FirstCheckDelay { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan Interval { get; init; } = UpdateSource.CheckInterval;

    /// <summary>Repeated "update now" requests within this window reuse the last answer from GitHub.</summary>
    public TimeSpan ManualCheckReuse { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Unattended installs only from %ProgramFiles%\WRApperture (never from a development folder).</summary>
    public bool CanInstallUnattended { get; init; } = UpdateSource.RunsFromInstallDirectory(UpdateProduct.Host);

    /// <summary>An empty folder only SYSTEM and Administrators can write to.</summary>
    public Func<string> PrepareDownloadFolder { get; init; } = () => UpdateFolder.Prepare("agent");
}

internal enum UpdateRequestOutcome
{
    UpToDate,
    Installing,
}

/// <summary>
/// Keeps the host on the latest release: checks ~2 minutes after start and then every 6 hours; with
/// AutoUpdate on, downloads the host setup, verifies its SHA-256 and runs it silently (the setup stops
/// this service, replaces the files and starts it again). Also serves GET/POST /api/update.
/// </summary>
internal sealed class HostUpdater(
    IUpdateChecker checker,
    IHostSetupRunner runner,
    IAgentSettingsStore settings,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    HostUpdaterOptions options,
    ILogger<HostUpdater> logger) : BackgroundService
{
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private ReleaseInfo? _latest;
    private DateTimeOffset? _checkedAt;
    private int _installing;
    private (Version Version, DateTimeOffset At)? _failed;

    public Version Current => options.CurrentVersion;

    /// <summary>The install started by the last request or scheduled check (tests wait on it).</summary>
    internal Task CurrentInstall { get; private set; } = Task.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(options.FirstCheckDelay, time, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunScheduledAsync(stoppingToken);
                await Task.Delay(options.Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Service stopping (possibly by the setup this class started).
        }
    }

    /// <summary>One scheduled round: check, and install when allowed. Failures only log (FR-002).</summary>
    internal async Task RunScheduledAsync(CancellationToken ct)
    {
        ReleaseInfo? release;
        try
        {
            release = await CheckAsync(TimeSpan.Zero, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Update check failed: {Error}", ex.Message);
            return;
        }

        if (release is null || !release.IsNewerThan(Current))
        {
            logger.LogInformation("Agent {Current} is up to date (latest release: {Latest})", Format(Current), release?.Tag ?? "none");
            return;
        }

        if (!settings.Load().AutoUpdate)
        {
            logger.LogInformation("Release {Latest} is available; automatic updates are off", release.Tag);
            return;
        }

        if (!options.CanInstallUnattended)
        {
            logger.LogInformation("Release {Latest} is available; not installing it unattended because the agent does not run from {Folder}",
                release.Tag, UpdateSource.InstallDirectory(UpdateProduct.Host));
            return;
        }

        if (_failed is { } failed && failed.Version == release.Version && time.GetUtcNow() - failed.At < options.Interval)
        {
            logger.LogInformation("Release {Latest} failed to install recently; next try after {Next}", release.Tag, failed.At + options.Interval);
            return;
        }

        await TryInstallAsync(release, "automatic update", ct);
    }

    /// <summary>GET /api/update: checks only when the last answer is older than the interval; a failure keeps what is known.</summary>
    public async Task<UpdateStatusDto> GetStatusAsync(CancellationToken ct)
    {
        try
        {
            await CheckAsync(options.Interval, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Update check failed: {Error}", ex.Message);
        }

        var latest = _latest;
        return new UpdateStatusDto(Format(Current), latest is null ? null : Format(latest.Version), latest?.IsNewerThan(Current) == true,
            settings.Load().AutoUpdate, _checkedAt);
    }

    /// <summary>POST /api/update: checks and, if a newer release exists, installs it in the background.</summary>
    /// <exception cref="HttpRequestException">GitHub could not be reached.</exception>
    public async Task<(UpdateRequestOutcome Outcome, Version? Latest)> UpdateNowAsync(CancellationToken ct)
    {
        var release = await CheckAsync(options.ManualCheckReuse, ct);
        if (release is null || !release.IsNewerThan(Current))
        {
            logger.LogInformation("Update requested by a client: agent {Current} is up to date", Format(Current));
            return (UpdateRequestOutcome.UpToDate, release?.Version);
        }

        logger.LogInformation("Update to {Latest} requested by a client", release.Tag);
        // Not tied to the request: the download outlives it; only the service stopping cancels it.
        _ = TryInstallAsync(release, "requested by a client", lifetime.ApplicationStopping);
        return (UpdateRequestOutcome.Installing, release.Version);
    }

    /// <summary>Single flight; reuses the last successful answer when it is younger than <paramref name="maxAge"/>.</summary>
    private async Task<ReleaseInfo?> CheckAsync(TimeSpan maxAge, CancellationToken ct)
    {
        await _checkLock.WaitAsync(ct);
        try
        {
            if (_checkedAt is { } at && maxAge > TimeSpan.Zero && time.GetUtcNow() - at < maxAge)
            {
                return _latest;
            }

            var release = await checker.GetLatestAsync(ct);
            (_latest, _checkedAt) = (release, time.GetUtcNow());
            return release;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    /// <summary>
    /// Never two at once (a second request just joins the running one, without waiting for it). Otherwise
    /// returns when the setup ends, though normally the setup stops this service first.
    /// </summary>
    private Task TryInstallAsync(ReleaseInfo release, string reason, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _installing, 1, 0) != 0)
        {
            logger.LogInformation("An update is already in progress; ignoring this one ({Reason})", reason);
            return Task.CompletedTask;
        }

        return CurrentInstall = InstallAsync(release, reason, ct);
    }

    private async Task InstallAsync(ReleaseInfo release, string reason, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Updating agent {Current} to {Latest} ({Reason}): downloading", Format(Current), release.Tag, reason);
            var folder = options.PrepareDownloadFolder();
            var setup = await checker.DownloadVerifiedAsync(release, UpdateProduct.Host, folder, ct);
            logger.LogInformation("Verified {Setup} (SHA-256 {Hash}); starting it silently, the service will restart", setup.Path, setup.Sha256);
            var exitCode = await runner.RunAsync(setup, ct);

            // Still running: the setup did not replace this service.
            if (exitCode != 0)
            {
                logger.LogError("Host setup {Latest} exited with code {ExitCode}; the agent was not updated", release.Tag, exitCode);
                _failed = (release.Version, time.GetUtcNow());
            }
            else
            {
                logger.LogWarning("Host setup {Latest} finished but this agent ({Current}) is still running", release.Tag, Format(Current));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Agent stopping while updating to {Latest}", release.Tag);
        }
        catch (Exception ex) when (ex is HttpRequestException or UpdateVerificationException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException or System.ComponentModel.Win32Exception
                                       or OperationCanceledException)
        {
            logger.LogError(ex, "Updating to {Latest} failed", release.Tag);
            _failed = (release.Version, time.GetUtcNow());
        }
        finally
        {
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    private static string Format(Version version) => ProductVersion.Format(version);

    public override void Dispose()
    {
        _checkLock.Dispose();
        base.Dispose();
    }
}
