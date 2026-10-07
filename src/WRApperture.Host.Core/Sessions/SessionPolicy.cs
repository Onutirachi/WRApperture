namespace WRApperture.Host.Core.Sessions;

/// <summary>Matches the user name a client connects with against Windows session user names.</summary>
public static class RdpUserMatcher
{
    /// <summary>Windows 10/11 derives local names of Microsoft accounts from the start of the e-mail (min. 5 chars).</summary>
    private const int MinMicrosoftAccountPrefix = 5;

    public static bool Matches(string? rdpUser, HostSession session)
    {
        if (string.IsNullOrWhiteSpace(rdpUser))
        {
            return false;
        }

        var name = rdpUser.Trim();
        var slash = name.LastIndexOf('\\');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        if (string.Equals(name, session.User, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var at = name.IndexOf('@');
        if (at <= 0)
        {
            return false;
        }

        var localPart = name[..at];
        return string.Equals(localPart, session.User, StringComparison.OrdinalIgnoreCase) ||
               (session.User.Length >= MinMicrosoftAccountPrefix &&
                localPart.StartsWith(session.User, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Decides which sessions prevent a RemoteApp connection. On Windows client editions a user cannot
/// have a desktop session and a RemoteApp session at once, and a disconnected desktop session would
/// simply be reconnected, so the blocking desktop sessions must be logged off.
/// Never selects RemoteApp sessions or sessions of other users.
/// </summary>
public static class SessionPolicy
{
    public static IReadOnlyList<HostSession> Blocking(IEnumerable<HostSession> sessions, string? rdpUser)
    {
        var desktops = sessions.Where(s => s.Kind == SessionKind.Desktop && !string.IsNullOrEmpty(s.User)).ToList();

        if (!string.IsNullOrWhiteSpace(rdpUser))
        {
            // A known user that matches nobody is someone else: never touch their sessions.
            return desktops.Where(s => RdpUserMatcher.Matches(rdpUser, s)).ToList();
        }

        // Unknown RDP user (credentials not saved in the client): assume it is whoever uses the host locally.
        var consoleUser = desktops.FirstOrDefault(s => s.IsConsole && s.State == SessionConnectState.Active)?.User;
        return consoleUser is null
            ? []
            : desktops.Where(s => string.Equals(s.User, consoleUser, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The host's screen is showing a RemoteApp session (a local logon attached to it): it needs a desktop shell.</summary>
    public static HostSession? ConsoleOnRemoteApp(IEnumerable<HostSession> sessions) =>
        sessions.FirstOrDefault(s => s.IsConsole && s.State == SessionConnectState.Active && s.Kind == SessionKind.RemoteApp);
}
