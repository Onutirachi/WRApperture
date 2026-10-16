using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Tests;

public class ChangesTests : IDisposable
{
    private readonly AgentFactory _factory = new();

    public ChangesTests() => _factory.Publisher.Apps.Add(FakePublisher.App("notepad", "Bloco de Notas"));

    public void Dispose() => _factory.Dispose();

    private async Task<ChangesDto> GetAsync(string? since = null)
    {
        var route = since is null ? ApiContract.Changes : $"{ApiContract.Changes}?since={Uri.EscapeDataString(since)}";
        var response = await _factory.CreateAuthorizedClient().GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChangesDto>())!;
    }

    [Fact]
    public async Task Requires_token()
    {
        var response = await _factory.CreateClient().GetAsync(ApiContract.Changes);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_version_answers_at_once()
    {
        var watch = Stopwatch.StartNew();

        var changes = await GetAsync();

        Assert.False(string.IsNullOrEmpty(changes.Version));
        Assert.True(watch.Elapsed < AgentFactory.ChangesMaxWait);
    }

    [Fact]
    public async Task Nothing_changed_waits_and_answers_the_same_version()
    {
        var current = await GetAsync();
        var watch = Stopwatch.StartNew();

        var changes = await GetAsync(current.Version);

        Assert.Equal(current.Version, changes.Version);
        Assert.True(watch.Elapsed >= AgentFactory.ChangesMaxWait - TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task A_published_app_ends_the_wait()
    {
        var current = await GetAsync();

        var waiting = GetAsync(current.Version);
        await Task.Delay(50);
        _factory.Publisher.Apps.Add(FakePublisher.App("calc", "Calculadora"));
        var changes = await waiting;

        Assert.NotEqual(current.Version, changes.Version);
    }

    [Fact]
    public async Task Renamed_app_and_vpn_change_the_version()
    {
        var first = await GetAsync();
        _factory.Publisher.Apps[0] = FakePublisher.App("notepad", "Notepad");
        await Task.Delay(50);
        var renamed = await GetAsync();
        _factory.Networks.All.Add(new NetworkProfile(Guid.NewGuid(), "VPN", NetworkCategory.Public, "Fortinet SSL VPN"));
        await Task.Delay(50);
        var vpn = await GetAsync();

        Assert.NotEqual(first.Version, renamed.Version);
        Assert.NotEqual(renamed.Version, vpn.Version);
    }

    [Fact]
    public async Task Hidden_apps_do_not_change_the_version()
    {
        var first = await GetAsync();
        _factory.Publisher.Apps.Add(FakePublisher.App("hidden", "Oculto", visible: false));
        await Task.Delay(50);

        Assert.Equal(first.Version, (await GetAsync()).Version);
    }

    [Fact]
    public async Task Host_info_announces_the_feature()
    {
        var info = await _factory.CreateAuthorizedClient().GetFromJsonAsync<HostInfoDto>(ApiContract.Host);

        Assert.Contains(AgentFeatures.Changes, info!.Features!);
    }
}
