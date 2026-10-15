using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

internal sealed class WindowsProcessList : IProcessList
{
    private const uint QueryLimitedInformation = 0x1000;

    public IReadOnlyList<RunningProcess> InSessions(IReadOnlySet<int> sessionIds)
    {
        var result = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                int session;
                try
                {
                    session = process.SessionId;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    continue; // exited
                }

                if (sessionIds.Contains(session))
                {
                    result.Add(new RunningProcess(process.Id, session, PathOf(process.Id)));
                }
            }
        }

        return result;
    }

    public bool Stop(int processId, string processName)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited || !string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            process.Kill();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false; // already gone, or not ours to end
        }
    }

    private static string? PathOf(int processId)
    {
        var handle = Native.OpenProcess(QueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
