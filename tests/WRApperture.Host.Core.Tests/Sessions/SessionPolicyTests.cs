using WRApperture.Host.Core.Sessions;

namespace WRApperture.Host.Core.Tests.Sessions;

public class SessionPolicyTests
{
    private static HostSession S(int id, string user, SessionKind kind, SessionConnectState state = SessionConnectState.Active, bool console = false) =>
        new(id, user, "PC", state, console, kind);

    private static void AssertPlan(int[] logoff, int[] disconnect, ReleasePlan plan)
    {
        Assert.Equal(logoff, plan.Logoff.Select(s => s.Id));
        Assert.Equal(disconnect, plan.Disconnect.Select(s => s.Id));
    }

    [Fact]
    public void Same_user_desktop_sessions_are_logged_off_in_any_state()
    {
        var plan = SessionPolicy.Plan(
        [
            S(1, "joao", SessionKind.Desktop, console: true),
            S(2, "joao", SessionKind.Desktop, SessionConnectState.Disconnected),
            S(3, "maria", SessionKind.Desktop, SessionConnectState.Disconnected),
        ], @"PC\joao");

        AssertPlan([1, 2], [], plan);
    }

    [Fact]
    public void Same_user_remote_desktop_session_is_logged_off_too()
    {
        // The user's scenario: logged on to the host through a regular (full) Remote Desktop session.
        var plan = SessionPolicy.Plan([S(7, "joao", SessionKind.Desktop)], "joao");

        AssertPlan([7], [], plan);
    }

    [Fact]
    public void Other_users_active_sessions_are_only_disconnected()
    {
        var plan = SessionPolicy.Plan(
        [
            S(1, "maria", SessionKind.Desktop, console: true),
            S(2, "pedro", SessionKind.Desktop, SessionConnectState.Disconnected),
        ], "joao@outlook.com");

        AssertPlan([], [1], plan);
    }

    [Fact]
    public void RemoteApp_sessions_of_the_user_are_never_touched()
    {
        var plan = SessionPolicy.Plan([S(1, "joao", SessionKind.RemoteApp)], "joao");

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Unknown_user_means_whoever_has_an_active_desktop_local_or_remote()
    {
        var plan = SessionPolicy.Plan(
        [
            S(7, "joao", SessionKind.Desktop),                                 // full Remote Desktop session
            S(2, "joao", SessionKind.Desktop, SessionConnectState.Disconnected),
            S(3, "maria", SessionKind.Desktop, SessionConnectState.Disconnected),
        ], null);

        AssertPlan([7, 2], [], plan);
    }

    [Fact]
    public void Opening_a_second_app_never_disconnects_our_own_remoteapp_session()
    {
        // Unknown user, an app already open: the active RemoteApp session is (probably) ours.
        Assert.True(SessionPolicy.Plan([S(5, "joao", SessionKind.RemoteApp)], null).IsEmpty);
        Assert.True(SessionPolicy.Plan([S(5, "joao", SessionKind.RemoteApp)], "joao").IsEmpty);
    }

    [Fact]
    public void Free_host_has_an_empty_plan()
    {
        Assert.True(SessionPolicy.Plan([S(2, "joao", SessionKind.Desktop, SessionConnectState.Disconnected)], null).IsEmpty);
        Assert.True(SessionPolicy.Plan([], "joao").IsEmpty);
    }

    [Fact]
    public void Console_on_remoteapp_is_detected_only_when_active_at_console()
    {
        Assert.Equal(4, SessionPolicy.ConsoleOnRemoteApp([S(4, "joao", SessionKind.RemoteApp, console: true)])!.Id);
        Assert.Null(SessionPolicy.ConsoleOnRemoteApp([S(4, "joao", SessionKind.RemoteApp)]));
        Assert.Null(SessionPolicy.ConsoleOnRemoteApp([S(4, "joao", SessionKind.Desktop, console: true)]));
    }

    [Theory]
    [InlineData("joao", "joao", true)]
    [InlineData(@"PC-ESCRITORIO\Joao", "joao", true)]
    [InlineData(@".\joao", "joao", true)]
    [InlineData("joao@empresa.com", "joao", true)]
    [InlineData("carlos.exemplo@example.com", "carlo", true)]
    [InlineData("carlos.exemplo@example.com", "carl", false)]
    [InlineData("maria", "joao", false)]
    [InlineData("", "joao", false)]
    [InlineData(null, "joao", false)]
    public void Rdp_user_matching(string? rdpUser, string sessionUser, bool expected)
    {
        Assert.Equal(expected, RdpUserMatcher.Matches(rdpUser, S(1, sessionUser, SessionKind.Desktop)));
    }
}
