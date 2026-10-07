using WRApperture.Host.Core.Sessions;

namespace WRApperture.Host.Core.Tests.Sessions;

public class SessionPolicyTests
{
    private static HostSession S(int id, string user, SessionKind kind, SessionConnectState state = SessionConnectState.Active, bool console = false) =>
        new(id, user, "PC", state, console, kind);

    [Fact]
    public void Same_user_desktop_sessions_block_in_any_state()
    {
        var sessions = new[]
        {
            S(1, "joao", SessionKind.Desktop, console: true),
            S(2, "joao", SessionKind.Desktop, SessionConnectState.Disconnected),
            S(3, "maria", SessionKind.Desktop, SessionConnectState.Disconnected),
        };

        Assert.Equal([1, 2], SessionPolicy.Blocking(sessions, @"PC\joao").Select(s => s.Id));
    }

    [Fact]
    public void RemoteApp_sessions_never_block()
    {
        var sessions = new[] { S(1, "joao", SessionKind.RemoteApp, SessionConnectState.Disconnected) };

        Assert.Empty(SessionPolicy.Blocking(sessions, "joao"));
    }

    [Fact]
    public void Other_users_are_never_selected_when_rdp_user_is_known()
    {
        var sessions = new[] { S(1, "maria", SessionKind.Desktop, console: true) };

        // "joao" has no session; Windows itself asks maria to release the host.
        Assert.Empty(SessionPolicy.Blocking(sessions, "joao@outlook.com"));
    }

    [Fact]
    public void Unknown_rdp_user_means_the_console_user()
    {
        var sessions = new[]
        {
            S(1, "joao", SessionKind.Desktop, console: true),
            S(2, "joao", SessionKind.Desktop, SessionConnectState.Disconnected),
            S(3, "maria", SessionKind.Desktop, SessionConnectState.Disconnected),
        };

        Assert.Equal([1, 2], SessionPolicy.Blocking(sessions, null).Select(s => s.Id));
    }

    [Fact]
    public void Unknown_user_and_nobody_at_the_console_blocks_nothing()
    {
        var sessions = new[] { S(2, "joao", SessionKind.Desktop, SessionConnectState.Disconnected) };

        Assert.Empty(SessionPolicy.Blocking(sessions, null));
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
