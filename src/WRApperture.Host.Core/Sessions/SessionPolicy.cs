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

/// <summary>What must happen to the host's sessions before a RemoteApp connection.</summary>
public sealed record ReleasePlan(IReadOnlyList<HostSession> Logoff, IReadOnlyList<HostSession> Disconnect)
{
    public bool IsEmpty => Logoff.Count == 0 && Disconnect.Count == 0;
}

/// <summary>
/// Windows client editions allow one active session, and a user cannot have a desktop session and a
/// RemoteApp session at once. Without preparation Windows asks on both ends ("another user is signed
/// in / will be disconnected"). The plan:
/// <list type="bullet">
/// <item>Desktop sessions of the connecting user → log off (a desktop session would be reconnected and RemoteApp fails).</item>
/// <item>Active sessions of other users → disconnect only: no prompt, and their work is preserved.</item>
/// <item>RemoteApp sessions are never touched (they are simply reconnected).</item>
/// </list>
/// When the client does not know its RDP user (credentials not saved), the users with an active
/// desktop session are assumed to be the connecting person.
/// <para>
/// Full desktop connections (<paramref name="desktop"/>) invert the first rule: the user's desktop
/// session is simply reconnected (programs kept), while the user's RemoteApp sessions are logged off
/// (reconnected as a desktop they would show no taskbar). Other users are disconnected as above.
/// </para>
/// </summary>
public static class SessionPolicy
{
    public static ReleasePlan Plan(IEnumerable<HostSession> sessions, string? rdpUser, bool desktop = false)
    {
        if (desktop)
        {
            return PlanDesktop(sessions, rdpUser);
        }

        var all = sessions.Where(s => !string.IsNullOrEmpty(s.User)).ToList();
        Func<HostSession, bool> isSameUser;

        if (!string.IsNullOrWhiteSpace(rdpUser))
        {
            isSameUser = s => RdpUserMatcher.Matches(rdpUser, s);
        }
        else
        {
            var activeDesktopUsers = all
                .Where(s => s.Kind == SessionKind.Desktop && s.State == SessionConnectState.Active)
                .Select(s => s.User)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            isSameUser = s => activeDesktopUsers.Contains(s.User);
        }

        var userKnown = !string.IsNullOrWhiteSpace(rdpUser);
        var logoff = all.Where(s => s.Kind == SessionKind.Desktop && isSameUser(s)).ToList();
        var disconnect = all.Where(s => s.State == SessionConnectState.Active && !isSameUser(s) &&
                                        // With an unknown user an active RemoteApp session may be our own
                                        // (opening a second app): disconnecting it would close the open apps.
                                        (userKnown || s.Kind != SessionKind.RemoteApp)).ToList();
        return new ReleasePlan(logoff, disconnect);
    }

    private static ReleasePlan PlanDesktop(IEnumerable<HostSession> sessions, string? rdpUser)
    {
        var all = sessions.Where(s => !string.IsNullOrEmpty(s.User)).ToList();
        Func<HostSession, bool> isSameUser;

        if (!string.IsNullOrWhiteSpace(rdpUser))
        {
            isSameUser = s => RdpUserMatcher.Matches(rdpUser, s);
        }
        else
        {
            // Unknown user: active desktops and RemoteApp sessions (only WRApperture creates them) are
            // assumed to be the connecting person's; the desktop is reused, the RemoteApps are closed.
            var activeUsers = all
                .Where(s => s.State == SessionConnectState.Active)
                .Select(s => s.User)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            isSameUser = s => s.Kind == SessionKind.RemoteApp || activeUsers.Contains(s.User);
        }

        var logoff = all.Where(s => s.Kind == SessionKind.RemoteApp && isSameUser(s)).ToList();
        var disconnect = all.Where(s => s.State == SessionConnectState.Active && !isSameUser(s)).ToList();
        return new ReleasePlan(logoff, disconnect);
    }

    /// <summary>The host's screen is showing a RemoteApp session (a local logon attached to it): it needs a desktop shell.</summary>
    public static HostSession? ConsoleOnRemoteApp(IEnumerable<HostSession> sessions) =>
        sessions.FirstOrDefault(s => s.IsConsole && s.State == SessionConnectState.Active && s.Kind == SessionKind.RemoteApp);
}
