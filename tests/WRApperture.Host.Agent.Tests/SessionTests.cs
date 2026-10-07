using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Agent.Sessions;
using WRApperture.Host.Core.Sessions;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Tests;

public class SessionTests : IDisposable
{
    private readonly AgentFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static HostSession S(int id, string user, SessionKind kind, SessionConnectState state = SessionConnectState.Active, bool console = false) =>
        new(id, user, "PC", state, console, kind);

    [Fact]
    public async Task Session_routes_require_token()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiContract.Session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(ApiContract.SessionRelease, new SessionReleaseRequestDto("joao"))).StatusCode);
        Assert.Empty(_factory.Sessions.LoggedOff);
    }

    [Fact]
    public async Task Status_reports_blocking_desktop_sessions_of_the_user()
    {
        _factory.Sessions.All.AddRange([S(1, "joao", SessionKind.Desktop, console: true), S(2, "maria", SessionKind.Desktop, SessionConnectState.Disconnected)]);

        var status = await _factory.CreateAuthorizedClient().GetFromJsonAsync<SessionStatusDto>($"{ApiContract.Session}?user=joao");

        Assert.True(status!.Busy);
        var busy = Assert.Single(status.Sessions);
        Assert.Equal(@"PC\joao", busy.User);
        Assert.True(busy.IsConsole);
        Assert.Equal(SessionActions.Logoff, busy.Action);
    }

    [Fact]
    public async Task Status_is_free_with_only_remoteapp_sessions()
    {
        _factory.Sessions.All.Add(S(3, "joao", SessionKind.RemoteApp, SessionConnectState.Disconnected));

        var status = await _factory.CreateAuthorizedClient().GetFromJsonAsync<SessionStatusDto>(ApiContract.Session);

        Assert.False(status!.Busy);
    }

    [Fact]
    public async Task Release_logs_off_only_the_users_desktop_sessions()
    {
        _factory.Sessions.All.AddRange(
        [
            S(1, "joao", SessionKind.Desktop, console: true),
            S(2, "joao", SessionKind.Desktop, SessionConnectState.Disconnected),
            S(3, "joao", SessionKind.RemoteApp, SessionConnectState.Disconnected),
            S(4, "maria", SessionKind.Desktop, SessionConnectState.Disconnected),
        ]);

        var response = await _factory.CreateAuthorizedClient().PostAsJsonAsync(ApiContract.SessionRelease, new SessionReleaseRequestDto(@"PC\joao"));
        var result = await response.Content.ReadFromJsonAsync<SessionReleaseResultDto>();

        Assert.Equal(2, result!.Released);
        Assert.False(result.Busy);
        Assert.Equal([1, 2], _factory.Sessions.LoggedOff);
    }

    [Fact]
    public async Task Release_with_unknown_user_logs_off_the_active_desktop_user_even_over_remote_desktop()
    {
        _factory.Sessions.All.AddRange([S(7, "joao", SessionKind.Desktop), S(4, "maria", SessionKind.Desktop, SessionConnectState.Disconnected)]);

        var result = await (await _factory.CreateAuthorizedClient()
            .PostAsJsonAsync(ApiContract.SessionRelease, new SessionReleaseRequestDto(null))).Content.ReadFromJsonAsync<SessionReleaseResultDto>();

        Assert.Equal([7], _factory.Sessions.LoggedOff);
        Assert.False(result!.Busy);
    }

    [Fact]
    public async Task Release_only_disconnects_another_users_active_session()
    {
        _factory.Sessions.All.Add(S(1, "maria", SessionKind.Desktop, console: true));

        var result = await (await _factory.CreateAuthorizedClient()
            .PostAsJsonAsync(ApiContract.SessionRelease, new SessionReleaseRequestDto("joao"))).Content.ReadFromJsonAsync<SessionReleaseResultDto>();

        Assert.Equal(1, result!.Released);
        Assert.False(result.Busy);
        Assert.Empty(_factory.Sessions.LoggedOff);
        Assert.Equal([1], _factory.Sessions.Disconnected);
    }

    [Fact]
    public async Task Desktop_mode_reuses_the_users_desktop_and_logs_off_its_remoteapps()
    {
        _factory.Sessions.All.AddRange(
        [
            S(1, "joao", SessionKind.Desktop, console: true),
            S(3, "joao", SessionKind.RemoteApp, SessionConnectState.Disconnected),
        ]);
        var client = _factory.CreateAuthorizedClient();

        var status = await client.GetFromJsonAsync<SessionStatusDto>($"{ApiContract.Session}?user=joao&mode=desktop");
        var busy = Assert.Single(status!.Sessions);
        Assert.Equal(SessionActions.Logoff, busy.Action);
        Assert.Equal(SessionModes.RemoteApp, busy.Kind);

        var result = await (await client.PostAsJsonAsync(ApiContract.SessionRelease, new SessionReleaseRequestDto("joao", SessionModes.Desktop)))
            .Content.ReadFromJsonAsync<SessionReleaseResultDto>();

        Assert.Equal([3], _factory.Sessions.LoggedOff);
        Assert.False(result!.Busy);
    }

    [Fact]
    public async Task Unknown_mode_is_rejected_without_touching_sessions()
    {
        _factory.Sessions.All.Add(S(1, "joao", SessionKind.Desktop, console: true));
        var client = _factory.CreateAuthorizedClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{ApiContract.Session}?mode=full")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(ApiContract.SessionRelease, new SessionReleaseRequestDto("joao", "full"))).StatusCode);
        Assert.Empty(_factory.Sessions.LoggedOff);
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData("remoteapp", true, false)]
    [InlineData("Desktop", true, true)]
    [InlineData("full", false, false)]
    public void Session_modes_parse(string? value, bool valid, bool desktop)
    {
        Assert.Equal(valid, SessionModes.TryParse(value, out var isDesktop));
        Assert.Equal(desktop, isDesktop);
    }

    [Fact]
    public void Watcher_starts_desktop_once_when_host_screen_shows_a_remoteapp_session()
    {
        _ = _factory.Server; // build the host
        var watcher = _factory.Services.GetRequiredService<SessionWatcher>();
        _factory.Sessions.All.Add(S(5, "joao", SessionKind.RemoteApp, console: true));

        Assert.Equal(5, watcher.Tick());
        Assert.Null(watcher.Tick());
        Assert.Equal([5], _factory.Sessions.ShellStarted);
    }

    [Fact]
    public void Watcher_ignores_remoteapp_sessions_not_on_the_host_screen()
    {
        _ = _factory.Server;
        var watcher = _factory.Services.GetRequiredService<SessionWatcher>();
        _factory.Sessions.All.Add(S(5, "joao", SessionKind.RemoteApp));

        Assert.Null(watcher.Tick());
        Assert.Empty(_factory.Sessions.ShellStarted);
    }
}
