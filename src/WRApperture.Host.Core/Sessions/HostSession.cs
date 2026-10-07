namespace WRApperture.Host.Core.Sessions;

public enum SessionKind
{
    /// <summary>No user shell detected (e.g. logon screen, services).</summary>
    Other,

    /// <summary>Full desktop session (explorer.exe running): blocks RemoteApp for the same user.</summary>
    Desktop,

    /// <summary>RemoteApp session (rdpshell/rdpinit without explorer).</summary>
    RemoteApp,
}

/// <summary>Mirrors WTS_CONNECTSTATE_CLASS.</summary>
public enum SessionConnectState
{
    Active = 0,
    Connected = 1,
    ConnectQuery = 2,
    Shadow = 3,
    Disconnected = 4,
    Idle = 5,
    Listen = 6,
    Reset = 7,
    Down = 8,
    Init = 9,
}

public sealed record HostSession(int Id, string User, string? Domain, SessionConnectState State, bool IsConsole, SessionKind Kind);

/// <summary>Windows Terminal Services session operations (requires LocalSystem for logoff/shell start).</summary>
public interface ISessionProvider
{
    /// <summary>User sessions only (sessions without a user are omitted).</summary>
    IReadOnlyList<HostSession> List();

    /// <summary>Logs off and waits for completion.</summary>
    void Logoff(int sessionId);

    /// <summary>Starts explorer.exe as the session's user, giving the session a desktop.</summary>
    void StartDesktopShell(int sessionId);
}
