using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WRApperture.Host.Core.Platform.Native;

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class CShellLink;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

internal static class Msi
{
    public const int ErrorSuccess = 0;
    public const int MaxGuidChars = 39;

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern uint MsiGetShortcutTargetW(string szShortcutPath, StringBuilder szProductCode, StringBuilder szFeatureId, StringBuilder szComponentCode);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    public static extern int MsiGetComponentPathW(string szProduct, string szComponent, StringBuilder? lpPathBuf, ref uint pcchBuf);
}

internal static class ShellLinkNative
{
    public const int MaxPath = 1024;

    public static (string Path, string Args, string IconPath, int IconIndex) Read(string shortcutPath)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0 /* STGM_READ */);

            var path = new StringBuilder(MaxPath);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            var args = new StringBuilder(MaxPath);
            link.GetArguments(args, args.Capacity);
            var icon = new StringBuilder(MaxPath);
            link.GetIconLocation(icon, icon.Capacity, out var iconIndex);
            return (path.ToString(), args.ToString(), icon.ToString(), iconIndex);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }
}
