using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Errors;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Core.Platform;

/// <summary>Local accounts through the NetApi32/WTS APIs. Group names are resolved from their SIDs (they are translated).</summary>
internal sealed class WindowsLocalAccounts(ILogger<WindowsLocalAccounts> logger) : ILocalAccounts
{
    internal static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    internal static readonly SecurityIdentifier RemoteDesktopUsers = new(WellKnownSidType.BuiltinRemoteDesktopUsersSid, null);

    /// <summary>
    /// A wrong password counts toward the account lockout threshold, so the blank-password probe runs at most
    /// once an hour per account and process.
    /// </summary>
    private static readonly TimeSpan PasswordProbeInterval = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, (PasswordStatus Status, DateTime CheckedUtc)> PasswordProbes = new();

    public LocalAccount? ConsoleUser()
    {
        var session = (int)Native.WTSGetActiveConsoleSessionId();
        var user = QuerySession(session, Native.WtsUserName);
        var domain = QuerySession(session, Native.WtsDomainName);
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(domain))
        {
            return null;
        }

        string sid;
        try
        {
            sid = ((SecurityIdentifier)new NTAccount(domain, user).Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            logger.LogWarning(ex, "Could not resolve the SID of {Domain}\\{User}", domain, user);
            return null;
        }

        var isLocal = string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        var groups = LocalGroupsOf(isLocal ? user : $@"{domain}\{user}");
        return new LocalAccount(user, domain, sid, isLocal,
            isLocal ? MicrosoftAccountOf(user) : null,
            groups.Contains(GroupName(Administrators)),
            groups.Contains(GroupName(RemoteDesktopUsers)));
    }

    public PasswordStatus GetPasswordStatus(LocalAccount account)
    {
        if (!account.IsLocal || account.MicrosoftAccount is not null)
        {
            return account.MicrosoftAccount is not null ? PasswordStatus.Set : PasswordStatus.Unknown;
        }

        if (PasswordProbes.TryGetValue(account.Sid, out var cached) && DateTime.UtcNow - cached.CheckedUtc < PasswordProbeInterval)
        {
            return cached.Status;
        }

        // A network logon with an empty password: Windows answers "account restriction" exactly when the
        // password is blank (LimitBlankPasswordUse) and "wrong password" when there is one.
        PasswordStatus status;
        if (Native.LogonUserW(account.UserName, ".", string.Empty, Native.Logon32LogonNetwork, Native.Logon32ProviderDefault, out var token))
        {
            Native.CloseHandle(token);
            status = PasswordStatus.Blank;
        }
        else
        {
            status = Marshal.GetLastPInvokeError() switch
            {
                Native.ErrorAccountRestriction => PasswordStatus.Blank,
                Native.ErrorLogonFailure => PasswordStatus.Set,
                _ => PasswordStatus.Unknown,
            };
        }

        PasswordProbes[account.Sid] = (status, DateTime.UtcNow);
        return status;
    }

    public void SetPassword(LocalAccount account, string password)
    {
        var info = new Native.UserInfo1003 { Password = password };
        var result = Native.NetUserSetInfo(null, account.UserName, 1003, ref info, out _);
        if (result != 0)
        {
            throw result == Native.NerrPasswordTooShort
                ? new AppValidationException("password", Strings.T("host.accounts.passwordPolicy"))
                : new Win32Exception(result, Strings.T("host.accounts.setPasswordFailed", result));
        }

        PasswordProbes.TryRemove(account.Sid, out _);
        logger.LogInformation("Password set for {User}", account.UserName);
    }

    public void AddToRemoteDesktopUsers(string sid) =>
        ChangeMembership(sid, add: true);

    public void RemoveFromRemoteDesktopUsers(string sid) =>
        ChangeMembership(sid, add: false);

    private void ChangeMembership(string sid, bool add)
    {
        var group = GroupName(RemoteDesktopUsers);
        var bytes = new byte[SecurityIdentifier.MaxBinaryLength];
        var identifier = new SecurityIdentifier(sid);
        identifier.GetBinaryForm(bytes, 0);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var member = new Native.LocalGroupMembersInfo0 { Sid = handle.AddrOfPinnedObject() };
            var result = add
                ? Native.NetLocalGroupAddMembers(null, group, 0, ref member, 1)
                : Native.NetLocalGroupDelMembers(null, group, 0, ref member, 1);
            if (result is not (0 or Native.ErrorMemberInAlias or Native.ErrorMemberNotInAlias))
            {
                throw new Win32Exception(result);
            }
        }
        finally
        {
            handle.Free();
        }

        logger.LogInformation("{Sid} {Change} {Group}", sid, add ? "added to" : "removed from", group);
    }

    /// <summary>"Remote Desktop Users", "Usuários da área de trabalho remota"…</summary>
    private static string GroupName(SecurityIdentifier sid)
    {
        var name = ((NTAccount)sid.Translate(typeof(NTAccount))).Value;
        return name[(name.IndexOf('\\') + 1)..];
    }

    private HashSet<string> LocalGroupsOf(string account)
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = Native.NetUserGetLocalGroups(null, account, 0, Native.LgIncludeIndirect, out var buffer, -1, out var read, out _);
        try
        {
            if (result != 0)
            {
                logger.LogWarning("NetUserGetLocalGroups({Account}) failed with {Error}", account, result);
                return groups;
            }

            var size = Marshal.SizeOf<IntPtr>();
            for (var i = 0; i < read; i++)
            {
                if (Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, i * size)) is { } name)
                {
                    groups.Add(name);
                }
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Native.NetApiBufferFree(buffer);
            }
        }

        return groups;
    }

    private static string? MicrosoftAccountOf(string user)
    {
        if (Native.NetUserGetInfo(null, user, 24, out var buffer) != 0)
        {
            return null;
        }

        try
        {
            var info = Marshal.PtrToStructure<Native.UserInfo24>(buffer);
            return info.InternetIdentity && !string.IsNullOrWhiteSpace(info.InternetPrincipalName) ? info.InternetPrincipalName : null;
        }
        finally
        {
            Native.NetApiBufferFree(buffer);
        }
    }

    private static string? QuerySession(int sessionId, int infoClass)
    {
        if (!Native.WTSQuerySessionInformationW(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Native.WTSFreeMemory(buffer);
        }
    }

    private static class Native
    {
        public const int WtsUserName = 5;
        public const int WtsDomainName = 7;
        public const int Logon32LogonNetwork = 3;
        public const int Logon32ProviderDefault = 0;
        public const int ErrorLogonFailure = 1326;
        public const int ErrorAccountRestriction = 1327;
        public const int ErrorMemberNotInAlias = 1377;
        public const int ErrorMemberInAlias = 1378;
        public const int NerrPasswordTooShort = 2245;
        public const int LgIncludeIndirect = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct UserInfo1003
        {
            public string Password;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct UserInfo24
        {
            [MarshalAs(UnmanagedType.Bool)] public bool InternetIdentity;
            public int Flags;
            public string? InternetProviderName;
            public string? InternetPrincipalName;
            public IntPtr UserSid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LocalGroupMembersInfo0
        {
            public IntPtr Sid;
        }

        [DllImport("kernel32.dll")]
        public static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool LogonUserW(string user, string domain, string password, int logonType, int provider, out IntPtr token);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetUserGetInfo(string? server, string user, int level, out IntPtr buffer);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetUserSetInfo(string? server, string user, int level, ref UserInfo1003 info, out int parameterError);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetUserGetLocalGroups(string? server, string user, int level, int flags, out IntPtr buffer,
            int preferredMaxLength, out int entriesRead, out int totalEntries);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetLocalGroupAddMembers(string? server, string group, int level, ref LocalGroupMembersInfo0 members, int count);

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        public static extern int NetLocalGroupDelMembers(string? server, string group, int level, ref LocalGroupMembersInfo0 members, int count);

        [DllImport("netapi32.dll")]
        public static extern int NetApiBufferFree(IntPtr buffer);
    }
}
