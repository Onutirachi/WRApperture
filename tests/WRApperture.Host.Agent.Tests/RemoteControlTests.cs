using System.Net;
using System.Net.Http.Json;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Publishing;
using WRApperture.Host.Core.Sessions;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Tests;

public class RemoteControlTests : IDisposable
{
    private readonly AgentFactory _factory = new();
    private readonly HttpClient _client;

    public RemoteControlTests()
    {
        _factory.Publisher.Apps.Add(FakePublisher.App("notepad", "Bloco de Notas"));
        _factory.Publisher.Apps.Add(FakePublisher.App("calc", "Calculadora"));
        _factory.Sessions.All.Add(new HostSession(2, "joao", "PC", SessionConnectState.Active, false, SessionKind.RemoteApp));
        _factory.Sessions.All.Add(new HostSession(1, "maria", "PC", SessionConnectState.Active, true, SessionKind.Desktop));
        _client = _factory.CreateAuthorizedClient();
    }

    public void Dispose() => _factory.Dispose();

    private static string Exe(string alias) => $@"C:\Secret\{alias}.exe";

    [Fact]
    public async Task Apps_open_in_a_RemoteApp_session_are_running()
    {
        _factory.Processes.All.Add(new RunningProcess(100, 2, Exe("notepad")));
        // The same program on the local desktop is not "open remotely".
        _factory.Processes.All.Add(new RunningProcess(200, 1, Exe("calc")));

        var apps = await _client.GetFromJsonAsync<List<AppSummaryDto>>(ApiContract.Apps);

        Assert.True(apps!.Single(a => a.Alias == "notepad").Running);
        Assert.False(apps!.Single(a => a.Alias == "calc").Running);
    }

    [Fact]
    public async Task Kill_ends_only_the_apps_remote_processes()
    {
        _factory.Processes.All.AddRange([new(100, 2, Exe("notepad")), new(101, 2, Exe("notepad")), new(200, 1, Exe("notepad")), new(300, 2, Exe("calc"))]);

        var response = await _client.PostAsync(ApiContract.KillApp("notepad"), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<AppStopResultDto>())!.Processes);
        Assert.Equal([100, 101], _factory.Processes.Stopped);
    }

    [Fact]
    public async Task Close_runs_taskkill_without_force_in_the_apps_session()
    {
        _factory.Processes.All.AddRange([new(100, 2, Exe("notepad")), new(101, 2, Exe("notepad"))]);

        var response = await _client.PostAsync(ApiContract.CloseApp("notepad"), null);

        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<AppStopResultDto>())!.Processes);
        var run = Assert.Single(_factory.Sessions.Hidden);
        Assert.Equal(2, run.Session);
        Assert.EndsWith("taskkill.exe", run.Program, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/PID 100 /PID 101", run.Arguments);
        Assert.Empty(_factory.Processes.Stopped);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("kill")]
    public async Task Unknown_app_is_not_found(string action)
    {
        var response = await _client.PostAsync($"{ApiContract.Apps}/nope/{action}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ending_the_session_logs_off_RemoteApp_sessions_only()
    {
        var response = await _client.PostAsJsonAsync(ApiContract.SessionEnd, new SessionEndRequestDto(null));

        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<SessionEndResultDto>())!.Ended);
        Assert.Equal([2], _factory.Sessions.LoggedOff);
    }

    [Fact]
    public async Task Ending_the_session_of_another_user_touches_nothing()
    {
        var response = await _client.PostAsJsonAsync(ApiContract.SessionEnd, new SessionEndRequestDto(@"PC\ana"));

        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<SessionEndResultDto>())!.Ended);
        Assert.Empty(_factory.Sessions.LoggedOff);
    }

    [Fact]
    public async Task Remote_routes_require_token()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(ApiContract.KillApp("notepad"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(ApiContract.SessionEnd, new SessionEndRequestDto(null))).StatusCode);
    }

    [Fact]
    public async Task Host_app_is_published_hidden_and_announced()
    {
        _factory.Files.Existing.Add(AgentFactory.ApproverPath);

        var info = await _client.GetFromJsonAsync<HostInfoDto>(ApiContract.Host);
        var apps = await _client.GetFromJsonAsync<List<AppSummaryDto>>(ApiContract.Apps);

        Assert.Equal(BuiltInApps.ManagerAlias, info!.ManagerAlias);
        Assert.Contains(AgentFeatures.RemoteControl, info.Features!);
        var published = Assert.Single(_factory.Publisher.Published);
        Assert.False(published.ShowInList);
        Assert.DoesNotContain(apps!, a => a.Alias == BuiltInApps.ManagerAlias);
    }

    [Fact]
    public async Task Host_app_published_by_hand_is_used_as_is()
    {
        _factory.Files.Existing.Add(AgentFactory.ApproverPath);
        _factory.Publisher.Apps.Add(FakePublisher.App("meu-host", "Host") with { ExecutablePath = AgentFactory.ApproverPath });

        var info = await _client.GetFromJsonAsync<HostInfoDto>(ApiContract.Host);

        Assert.Equal("meu-host", info!.ManagerAlias);
        Assert.Empty(_factory.Publisher.Published);
    }

    [Fact]
    public async Task No_host_app_installed_means_no_alias()
    {
        var info = await _client.GetFromJsonAsync<HostInfoDto>(ApiContract.Host);

        Assert.Null(info!.ManagerAlias);
    }
}
