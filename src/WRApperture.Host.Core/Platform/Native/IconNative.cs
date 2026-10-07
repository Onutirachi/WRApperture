using System.Runtime.InteropServices;

namespace WRApperture.Host.Core.Platform.Native;

internal static class IconNative
{
    public const uint ExtractError = 0xFFFFFFFF;
    public const uint SiidApplication = 2;
    public const uint ShgsiIcon = 0x000000100;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIconsW(
        string szFileName, int nIconIndex, int cxIcon, int cyIcon,
        IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll")]
    public static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref ShStockIconInfo psii);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ShStockIconInfo
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }
}
