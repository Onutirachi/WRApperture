using System.Runtime.InteropServices;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

/// <summary>Reads "Sleep after" (plugged in) of the active power plan.</summary>
internal sealed class WindowsPowerSettings : IPowerSettings
{
    private static readonly Guid SleepSubgroup = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
    private static readonly Guid StandbyIdle = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");

    public TimeSpan? SleepAfter()
    {
        if (Native.PowerGetActiveScheme(IntPtr.Zero, out var scheme) != 0)
        {
            return null;
        }

        try
        {
            var subgroup = SleepSubgroup;
            var setting = StandbyIdle;
            return Native.PowerReadACValueIndex(IntPtr.Zero, scheme, ref subgroup, ref setting, out var seconds) == 0 && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : null;
        }
        finally
        {
            Native.LocalFree(scheme);
        }
    }

    private static class Native
    {
        [DllImport("powrprof.dll")]
        public static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        public static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, ref Guid subGroupOfPowerSettingsGuid,
            ref Guid powerSettingGuid, out uint acValueIndex);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr memory);
    }
}
