namespace WRApperture.Host.Core.Sessions;

public enum SessionKind
{
    /// <summary>No user shell detected (e.g. logon screen, services).</summary>
    Other,

    /// <summary>Full desktop session (explorer.exe running as the shell): blocks RemoteApp for the same user.</summary>
    Desktop,

    /// <summary>RemoteApp session (rdpshell/rdpinit without explorer).</summary>
    RemoteApp,
}

/// <summary>Tells the desktop shell apart from other Explorer processes.</summary>
public static class DesktopShell
{
    /// <summary>
    /// Windows starts the shell as a bare "C:\Windows\Explorer.EXE". Explorers with arguments only open something
    /// (a folder, a program: "explorer.exe C:\…\FortiTray.exe") or serve others ("/factory,{…} -Embedding") and do
    /// not make a session a desktop one. An unknown command line counts as the shell (the old rule: any Explorer).
    /// </summary>
    public static bool IsShell(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return true;
        }

        var text = commandLine.Trim();
        string arguments;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            arguments = end < 0 ? string.Empty : text[(end + 1)..];
        }
        else
        {
            var space = text.IndexOf(' ');
            arguments = space < 0 ? string.Empty : text[(space + 1)..];
        }

        return arguments.Trim().Length == 0;
    }
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

/// <param name="Sid">The user's security identifier, when it could be resolved.</param>
public sealed record HostSession(int Id, string User, string? Domain, SessionConnectState State, bool IsConsole, SessionKind Kind,
    string? Sid = null);

/// <summary>Windows Terminal Services session operations (requires LocalSystem for logoff/shell start).</summary>
public interface ISessionProvider
{
    /// <summary>User sessions only (sessions without a user are omitted).</summary>
    IReadOnlyList<HostSession> List();

    /// <summary>Forced logoff; waits for completion.</summary>
    void Logoff(int sessionId);

    /// <summary>Disconnects (session and its programs stay alive); waits for completion.</summary>
    void Disconnect(int sessionId);

    /// <summary>Starts explorer.exe as the session's user, giving the session a desktop.</summary>
    void StartDesktopShell(int sessionId);

    /// <summary>Starts a program as the session's user, on its screen. Returns the process id.</summary>
    int StartProcess(int sessionId, string executable, string? arguments);
}
