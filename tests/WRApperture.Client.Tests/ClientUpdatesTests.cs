using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Storage;
using WRApperture.Client.Core.Updates;
using WRApperture.Shared;
using WRApperture.Shared.Updates;

namespace WRApperture.Client.Tests;

public sealed class ClientUpdatesTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeChecker _checker = new();
    private readonly ManualTime _time = new();
    private readonly ClientUpdates _updates;

    public ClientUpdatesTests() => _updates = new ClientUpdates(_checker, _dir.Paths, _time, current: new Version(1, 0, 0));

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Newer_release_is_an_update()
    {
        _checker.Latest = Release("1.1.0");

        await _updates.CheckAsync(manual: false);

        Assert.True(_updates.IsUpdateAvailable);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.0")]
    public async Task Same_or_older_release_is_not(string version)
    {
        _checker.Latest = Release(version);

        await _updates.CheckAsync(manual: false);

        Assert.False(_updates.IsUpdateAvailable);
    }

    [Fact]
    public async Task One_check_per_interval_and_manual_checks_reuse_a_minute_old_answer()
    {
        await _updates.CheckAsync(manual: false);
        await _updates.CheckAsync(manual: false);
        await _updates.CheckAsync(manual: true);
        Assert.Equal(1, _checker.Checks);

        _time.Now += ClientUpdates.ManualCheckReuse;
        await _updates.CheckAsync(manual: true);
        Assert.Equal(2, _checker.Checks);

        _time.Now += UpdateSource.CheckInterval;
        await _updates.CheckAsync(manual: false);
        Assert.Equal(3, _checker.Checks);
    }

    [Fact]
    public async Task A_failed_check_keeps_the_previous_answer_and_is_retried()
    {
        _checker.Latest = Release("1.1.0");
        await _updates.CheckAsync(manual: false);
        _checker.Error = new HttpRequestException("offline");

        _time.Now += ClientUpdates.ManualCheckReuse;

        await Assert.ThrowsAsync<HttpRequestException>(() => _updates.CheckAsync(manual: true));
        await Assert.ThrowsAsync<HttpRequestException>(() => _updates.CheckAsync(manual: true)); // not cached as "checked"

        Assert.True(_updates.IsUpdateAvailable);
        Assert.Equal(3, _checker.Checks);
    }

    [Fact]
    public async Task Download_empties_the_folder_and_asks_for_the_client_setup()
    {
        _checker.Latest = Release("1.1.0");
        Directory.CreateDirectory(_dir.Paths.Updates);
        File.WriteAllText(Path.Combine(_dir.Paths.Updates, "old.exe"), "x");

        var setup = await _updates.DownloadAsync((await _updates.CheckAsync(manual: false))!);

        Assert.Equal(UpdateProduct.Client, _checker.DownloadedProduct);
        Assert.Equal(Path.Combine(_dir.Paths.Updates, "WRApperture-Client-Setup-1.1.0.exe"), setup.Path);
        Assert.False(File.Exists(Path.Combine(_dir.Paths.Updates, "old.exe")));
    }

    [Theory]
    [InlineData("1.0.0", true, HostUpdateState.CanUpdate)]
    [InlineData("1.0.0", false, HostUpdateState.UpdateOnHost)] // agents before self-update
    [InlineData("1.1.0", true, HostUpdateState.Current)]
    [InlineData("1.2.0", true, HostUpdateState.Current)] // a host newer than the release: nothing to say
    [InlineData(null, true, HostUpdateState.Current)] // not synced since this version: unknown
    public async Task Compares_hosts_with_the_latest_release(string? agentVersion, bool selfUpdate, HostUpdateState expected)
    {
        _checker.Latest = Release("1.1.0");
        await _updates.CheckAsync(manual: false);
        var host = Samples.Host() with
        {
            AgentVersion = agentVersion,
            AgentFeatures = selfUpdate ? [AgentFeatures.DesktopSession, AgentFeatures.SelfUpdate] : [AgentFeatures.DesktopSession],
        };

        Assert.Equal(expected, _updates.HostState(host));
    }

    [Fact]
    public void Hosts_are_current_before_any_check() =>
        Assert.Equal(HostUpdateState.Current, _updates.HostState(Samples.Host() with { AgentVersion = "0.1.0" }));

    private static ReleaseInfo Release(string version) => new(Version.Parse(version), "v" + version, null, null, []);

    private sealed class FakeChecker : IUpdateChecker
    {
        public ReleaseInfo? Latest { get; set; }
        public Exception? Error { get; set; }
        public int Checks { get; private set; }
        public UpdateProduct? DownloadedProduct { get; private set; }

        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
        {
            Checks++;
            return Error is null ? Task.FromResult(Latest) : Task.FromException<ReleaseInfo?>(Error);
        }

        public Task<VerifiedSetup> DownloadVerifiedAsync(ReleaseInfo release, UpdateProduct product, string targetDirectory,
            CancellationToken ct = default)
        {
            DownloadedProduct = product;
            return Task.FromResult(new VerifiedSetup(Path.Combine(targetDirectory, UpdateSource.SetupAssetName(product, release.Version)),
                "abc", release.Version));
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
