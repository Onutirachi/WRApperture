using System.Net;
using System.Net.Http.Json;
using WRApperture.Shared;
using WRApperture.Shared.Updates;

namespace WRApperture.Host.Agent.Tests;

/// <summary>/api/update and the scheduled updater, with GitHub and the setup replaced by fakes.</summary>
public class UpdateTests : IDisposable
{
    private readonly AgentFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Update_routes_require_token()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiContract.Update)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(ApiContract.Update, null)).StatusCode);
        Assert.Empty(_factory.Setups.Runs);
    }

    [Fact]
    public async Task Host_announces_self_update()
    {
        var info = await _factory.CreateAuthorizedClient().GetFromJsonAsync<HostInfoDto>(ApiContract.Host);

        Assert.Contains(AgentFeatures.SelfUpdate, info!.Features!);
    }

    [Fact]
    public async Task Status_reports_versions_and_the_setting()
    {
        _factory.Updates.Latest = FakeUpdateChecker.Release("1.2.0");
        _factory.Settings.AutoUpdate = false;

        var status = await _factory.CreateAuthorizedClient().GetFromJsonAsync<UpdateStatusDto>(ApiContract.Update);

        Assert.Equal("1.0.0", status!.Current);
        Assert.Equal("1.2.0", status.Latest);
        Assert.True(status.Available);
        Assert.False(status.AutoUpdate);
        Assert.Equal(_factory.Time.Now, status.CheckedAtUtc);
    }

    [Fact]
    public async Task Status_reuses_a_recent_check_and_survives_github_failures()
    {
        var client = _factory.CreateAuthorizedClient();
        _factory.Updates.Latest = FakeUpdateChecker.Release("1.0.0");
        await client.GetAsync(ApiContract.Update);
        await client.GetAsync(ApiContract.Update);
        Assert.Equal(1, _factory.Updates.Checks);

        _factory.Time.Now += UpdateSource.CheckInterval;
        _factory.Updates.CheckError = new HttpRequestException("offline");
        var status = await client.GetFromJsonAsync<UpdateStatusDto>(ApiContract.Update);

        Assert.Equal(2, _factory.Updates.Checks);
        Assert.Equal("1.0.0", status!.Latest); // what was known before
        Assert.False(status.Available);
    }

    [Fact]
    public async Task Update_now_installs_a_newer_release_in_the_background()
    {
        _factory.Updates.Latest = FakeUpdateChecker.Release("1.1.0");
        // A body is ignored: nothing in the request decides what gets downloaded or run.
        var response = await _factory.CreateAuthorizedClient().PostAsJsonAsync(ApiContract.Update, new { url = "https://evil.example/x.exe" });
        await _factory.Updater.CurrentInstall;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UpdateRequestResultDto>();
        Assert.Equal(new UpdateRequestResultDto(UpdateOutcomes.Installing, "1.0.0", "1.1.0"), result);
        Assert.Equal((UpdateProduct.Host, @"C:\Fake\updates\agent"), Assert.Single(_factory.Updates.Downloads));
        Assert.Equal(@"C:\Fake\updates\agent\WRApperture-Host-Setup-1.1.0.exe", Assert.Single(_factory.Setups.Runs).Path);
    }

    [Fact]
    public async Task Update_now_works_even_with_automatic_updates_off()
    {
        _factory.Updates.Latest = FakeUpdateChecker.Release("1.1.0");
        _factory.Settings.AutoUpdate = false;

        var response = await _factory.CreateAuthorizedClient().PostAsync(ApiContract.Update, null);
        await _factory.Updater.CurrentInstall;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(_factory.Setups.Runs);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.9")]
    [InlineData(null)]
    public async Task Update_now_when_up_to_date_installs_nothing(string? latest)
    {
        _factory.Updates.Latest = latest is null ? null : FakeUpdateChecker.Release(latest);

        var response = await _factory.CreateAuthorizedClient().PostAsync(ApiContract.Update, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UpdateRequestResultDto>();
        Assert.Equal(UpdateOutcomes.UpToDate, result!.Outcome);
        Assert.Equal(latest, result.Latest);
        Assert.Empty(_factory.Updates.Downloads);
    }

    [Fact]
    public async Task Update_now_reports_an_unreachable_github()
    {
        _factory.Updates.CheckError = new HttpRequestException("offline");

        var response = await _factory.CreateAuthorizedClient().PostAsync(ApiContract.Update, null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(ApiContract.Errors.UpdateCheckFailed, (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Error);
    }

    [Fact]
    public async Task Never_two_updates_at_once()
    {
        _factory.Updates.Latest = FakeUpdateChecker.Release("1.1.0");
        _factory.Setups.Gate = new TaskCompletionSource<int>();
        var client = _factory.CreateAuthorizedClient();

        await client.PostAsync(ApiContract.Update, null);
        var install = _factory.Updater.CurrentInstall;
        var second = await client.PostAsync(ApiContract.Update, null);
        await _factory.Updater.RunScheduledAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode); // the running one is what will happen
        Assert.Single(_factory.Setups.Runs);
        _factory.Setups.Gate.SetResult(1);
        await install;
    }

    [Fact]
    public async Task Scheduled_check_installs_only_with_automatic_updates_on()
    {
        _factory.Updates.Latest = FakeUpdateChecker.Release("2.0.0");
        _factory.Settings.AutoUpdate = false;
        await _factory.Updater.RunScheduledAsync(CancellationToken.None);
        Assert.Empty(_factory.Setups.Runs);

        _factory.Settings.AutoUpdate = true;
        await _factory.Updater.RunScheduledAsync(CancellationToken.None);
        Assert.Single(_factory.Setups.Runs);
    }

    [Fact]
    public async Task Scheduled_check_survives_github_failures()
    {
        _factory.Updates.CheckError = new HttpRequestException("offline");

        await _factory.Updater.RunScheduledAsync(CancellationToken.None);

        Assert.Empty(_factory.Updates.Downloads);
    }

    [Fact]
    public async Task A_failed_version_is_retried_once_per_interval()
    {
        _factory.Updates.Latest = FakeUpdateChecker.Release("1.1.0");
        _factory.Updates.DownloadError = new UpdateVerificationException("checksum mismatch");

        await _factory.Updater.RunScheduledAsync(CancellationToken.None);
        await _factory.Updater.RunScheduledAsync(CancellationToken.None);
        Assert.Single(_factory.Updates.Downloads);
        Assert.Empty(_factory.Setups.Runs); // a file that failed verification is never run

        _factory.Time.Now += UpdateSource.CheckInterval;
        _factory.Updates.DownloadError = null;
        await _factory.Updater.RunScheduledAsync(CancellationToken.None);
        Assert.Equal(2, _factory.Updates.Downloads.Count);
        Assert.Single(_factory.Setups.Runs);
    }

    [Fact]
    public async Task A_failing_setup_counts_as_a_failed_version()
    {
        _factory.Updates.Latest = FakeUpdateChecker.Release("1.1.0");
        _factory.Setups.ExitCode = 2;

        await _factory.Updater.RunScheduledAsync(CancellationToken.None);
        await _factory.Updater.RunScheduledAsync(CancellationToken.None);

        Assert.Single(_factory.Setups.Runs);
    }
}
