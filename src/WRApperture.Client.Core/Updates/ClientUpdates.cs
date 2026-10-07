using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Storage;
using WRApperture.Shared;
using WRApperture.Shared.Updates;

namespace WRApperture.Client.Core.Updates;

public enum HostUpdateState
{
    /// <summary>Same as the latest release (or unknown: nothing is said).</summary>
    Current,

    /// <summary>Older, and the agent can update itself (POST /api/update).</summary>
    CanUpdate,

    /// <summary>Older, from before self-update: it must be updated on the host.</summary>
    UpdateOnHost,
}

/// <summary>
/// The client's view of the latest release: one check serves both its own update and the comparison
/// with each host's agent. Checks at most once per interval (manual checks reuse a minute-old answer).
/// </summary>
public sealed class ClientUpdates(IUpdateChecker checker, ClientPaths paths, TimeProvider time, ILogger<ClientUpdates>? logger = null,
    Version? current = null)
{
    public static readonly TimeSpan ManualCheckReuse = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger = logger ?? NullLogger<ClientUpdates>.Instance;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTimeOffset? _checkedAt;

    public Version Current { get; } = current ?? ProductVersion.Current;

    /// <summary>The latest release from the last successful check (null before it, or when none is published).</summary>
    public ReleaseInfo? Latest { get; private set; }

    public bool IsUpdateAvailable => Latest?.IsNewerThan(Current) == true;

    /// <exception cref="HttpRequestException">GitHub could not be reached (the previous answer is kept).</exception>
    public async Task<ReleaseInfo?> CheckAsync(bool manual, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var maxAge = manual ? ManualCheckReuse : UpdateSource.CheckInterval;
            if (_checkedAt is { } at && time.GetUtcNow() - at < maxAge)
            {
                return Latest;
            }

            Latest = await checker.GetLatestAsync(ct);
            _checkedAt = time.GetUtcNow();
            _logger.LogInformation("Latest release: {Latest} (this client: {Current})", Latest?.Tag ?? "none", ProductVersion.Format(Current));
            return Latest;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Downloads the client setup of <see cref="Latest"/> into an emptied folder and verifies it.</summary>
    /// <exception cref="UpdateVerificationException">Never run anything then.</exception>
    public async Task<VerifiedSetup> DownloadAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        if (Directory.Exists(paths.Updates))
        {
            Directory.Delete(paths.Updates, recursive: true);
        }

        var setup = await checker.DownloadVerifiedAsync(release, UpdateProduct.Client, paths.Updates, ct);
        _logger.LogInformation("Downloaded and verified {Setup} (SHA-256 {Hash})", setup.Path, setup.Sha256);
        return setup;
    }

    /// <summary>How <paramref name="host"/>'s agent (as of its last sync) compares with <see cref="Latest"/>.</summary>
    public HostUpdateState HostState(PairedHost host)
    {
        if (Latest is not { } latest || !ProductVersion.IsOlder(host.AgentVersion, latest.Version))
        {
            return HostUpdateState.Current;
        }

        return host.Supports(AgentFeatures.SelfUpdate) ? HostUpdateState.CanUpdate : HostUpdateState.UpdateOnHost;
    }
}
