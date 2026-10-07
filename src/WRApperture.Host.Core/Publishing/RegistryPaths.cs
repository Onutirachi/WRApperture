namespace WRApperture.Host.Core.Publishing;

/// <summary>HKLM-relative registry locations used by the Core.</summary>
internal static class RegistryPaths
{
    public const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    public const string AllowList = CurrentVersion + @"\Terminal Server\TSAppAllowList";
    public const string Applications = AllowList + @"\Applications";

    public const string TerminalServer = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
    public const string RdpTcp = TerminalServer + @"\WinStations\RDP-Tcp";

    public const string ToolRoot = @"SOFTWARE\WRApperture";
    public const string PreparationSnapshot = ToolRoot + @"\PreparationSnapshot";

    /// <summary>DWORD value written on apps created by this tool.</summary>
    public const string ManagedMarker = "WRApperture";

    public static string App(string alias) => $@"{Applications}\{alias}";
}
