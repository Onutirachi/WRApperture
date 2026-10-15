using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WRApperture.Host.Core.Sessions;

namespace WRApperture.Host.Core.Platform;

internal sealed class WindowsSessionProvider(ILogger<WindowsSessionProvider> logger) : ISessionProvider
{
    private static readonly IntPtr CurrentServer = IntPtr.Zero;

    public IReadOnlyList<HostSession> List()
    {
        var consoleId = (int)Native.WTSGetActiveConsoleSessionId();
        var processesBySession = Process.GetProcesses()
            .GroupBy(p => SafeSessionId(p))
            .ToDictionary(g => g.Key, g => g.Select(SafeName).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var result = new List<HostSession>();
        if (!Native.WTSEnumerateSessionsW(CurrentServer, 0, 1, out var buffer, out var count))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            var size = Marshal.SizeOf<Native.WtsSessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<Native.WtsSessionInfo>(buffer + (i * size));
                var user = Query(info.SessionId, Native.WtsUserName);
                if (string.IsNullOrEmpty(user))
                {
                    continue;
                }

                var names = processesBySession.GetValueOrDefault(info.SessionId) ?? [];
                var kind = names.Contains("explorer") ? SessionKind.Desktop
                    : names.Contains("rdpshell") || names.Contains("rdpinit") ? SessionKind.RemoteApp
                    : SessionKind.Other;

                var domain = Query(info.SessionId, Native.WtsDomainName);
                result.Add(new HostSession(info.SessionId, user, domain,
                    (SessionConnectState)info.State, info.SessionId == consoleId, kind, SidOf(domain, user)));
            }
        }
        finally
        {
            Native.WTSFreeMemory(buffer);
        }

        return result;
    }

    private static readonly TimeSpan ForcedLogoffWait = TimeSpan.FromSeconds(25);

    /// <summary>
    /// WTSLogoffSession is a "polite" logoff: apps with unsaved work can hold it and Windows asks the
    /// user in that session to confirm. Running "shutdown /l /f" inside the session forces it
    /// (EWX_FORCE), so nobody has to confirm. WTSLogoffSession remains the fallback.
    /// </summary>
    public void Logoff(int sessionId)
    {
        var user = Query(sessionId, Native.WtsUserName);
        try
        {
            var shutdown = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
            StartInSession(sessionId, shutdown, "/l /f");
            var deadline = DateTime.UtcNow + ForcedLogoffWait;
            while (DateTime.UtcNow < deadline)
            {
                if (!string.Equals(Query(sessionId, Native.WtsUserName), user, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrEmpty(Query(sessionId, Native.WtsUserName)))
                {
                    logger.LogInformation("Session {Session} of {User} logged off (forced)", sessionId, user);
                    return;
                }

                Thread.Sleep(500);
            }

            logger.LogWarning("Forced logoff of session {Session} did not finish in time; falling back to WTSLogoffSession", sessionId);
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "Forced logoff of session {Session} could not start; falling back to WTSLogoffSession", sessionId);
        }

        if (!Native.WTSLogoffSession(CurrentServer, sessionId, bWait: true))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"WTSLogoffSession({sessionId}) failed");
        }
    }

    public void Disconnect(int sessionId)
    {
        if (!Native.WTSDisconnectSession(CurrentServer, sessionId, bWait: true))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"WTSDisconnectSession({sessionId}) failed");
        }

        logger.LogInformation("Session {Session} disconnected", sessionId);
    }

    public void StartDesktopShell(int sessionId)
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var pid = StartInSession(sessionId, explorer, null);
        logger.LogInformation("Started desktop shell in session {Session} (pid {Pid})", sessionId, pid);
    }

    public int StartProcess(int sessionId, string executable, string? arguments)
    {
        var pid = StartInSession(sessionId, executable, arguments, showWindow: true);
        logger.LogInformation("Started {Program} in session {Session} (pid {Pid})", Path.GetFileName(executable), sessionId, pid);
        return pid;
    }

    /// <summary>Starts a process as the session's user on its interactive desktop (requires LocalSystem).</summary>
    private static int StartInSession(int sessionId, string executable, string? arguments, bool showWindow = false)
    {
        if (!Native.WTSQueryUserToken(sessionId, out var userToken))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "WTSQueryUserToken failed (requires LocalSystem)");
        }

        var environment = IntPtr.Zero;
        try
        {
            if (!Native.CreateEnvironmentBlock(out environment, userToken, false))
            {
                environment = IntPtr.Zero;
            }

            var startup = new Native.StartupInfo { cb = Marshal.SizeOf<Native.StartupInfo>(), lpDesktop = @"winsta0\default" };
            // lpCommandLine must be writable and, when given, start with the quoted executable.
            var commandLine = arguments is null ? null : $"\"{executable}\" {arguments}";
            if (!Native.CreateProcessAsUserW(userToken, executable, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    Native.CreateUnicodeEnvironment | (showWindow ? 0 : Native.CreateNoWindow), environment,
                    showWindow ? Path.GetDirectoryName(executable) : null, ref startup, out var process))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateProcessAsUser({Path.GetFileName(executable)}) failed");
            }

            Native.CloseHandle(process.hThread);
            Native.CloseHandle(process.hProcess);
            return process.dwProcessId;
        }
        finally
        {
            if (environment != IntPtr.Zero)
            {
                Native.DestroyEnvironmentBlock(environment);
            }

            Native.CloseHandle(userToken);
        }
    }

    private static string? Query(int sessionId, int infoClass)
    {
        if (!Native.WTSQuerySessionInformationW(CurrentServer, sessionId, infoClass, out var buffer, out _))
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

    private static string? SidOf(string? domain, string user)
    {
        try
        {
            var account = string.IsNullOrEmpty(domain) ? new System.Security.Principal.NTAccount(user) : new System.Security.Principal.NTAccount(domain, user);
            return ((System.Security.Principal.SecurityIdentifier)account.Translate(typeof(System.Security.Principal.SecurityIdentifier))).Value;
        }
        catch (Exception ex) when (ex is System.Security.Principal.IdentityNotMappedException or SystemException)
        {
            return null;
        }
    }

    private static int SafeSessionId(Process p)
    {
        try
        {
            return p.SessionId;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return -1;
        }
    }

    private static string SafeName(Process p)
    {
        try
        {
            return p.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static class Native
    {
        public const int WtsUserName = 5;
        public const int WtsDomainName = 7;
        public const uint CreateUnicodeEnvironment = 0x00000400;
        public const uint CreateNoWindow = 0x08000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct WtsSessionInfo
        {
            public int SessionId;
            public IntPtr pWinStationName;
            public int State;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct StartupInfo
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll")]
        public static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSEnumerateSessionsW(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSLogoffSession(IntPtr server, int sessionId, bool bWait);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSDisconnectSession(IntPtr server, int sessionId, bool bWait);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessAsUserW(IntPtr token, string applicationName, string? commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
            IntPtr environment, string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
