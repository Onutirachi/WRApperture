namespace WRApperture.Host.Core.Publishing;

/// <summary>HKLM-relative registry locations used by the Core.</summary>
internal static class RegistryPaths
{
    public const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    public const string AllowList = CurrentVersion + @"\Terminal Server\TSAppAllowList";
    public const string Applications = AllowList + @"\Applications";

    public const string TerminalServer = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
    public const string RdpTcp = TerminalServer + @"\WinStations\RDP-Tcp";

    /// <summary>Group Policy values here override <see cref="TerminalServer"/> (e.g. fDenyTSConnections).</summary>
    public const string TerminalServicesPolicy = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

    public const string TermService = @"SYSTEM\CurrentControlSet\Services\TermService";

    /// <summary>DevicePasswordLessBuildVersion = 2: "only Windows Hello for Microsoft accounts" (no password sign-in).</summary>
    public const string PasswordLess = CurrentVersion + @"\PasswordLess\Device";

    public const string ToolRoot = @"SOFTWARE\WRApperture";
    public const string PreparationSnapshot = ToolRoot + @"\PreparationSnapshot";
    public const string AgentSettings = ToolRoot + @"\Agent";

    /// <summary>DWORD value written on apps created by this tool.</summary>
    public const string ManagedMarker = "WRApperture";

    public static string App(string alias) => $@"{Applications}\{alias}";
}
