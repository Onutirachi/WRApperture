using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace WRApperture.Host.Manager.Views;

/// <summary>
/// File drop that also works when this window runs elevated. Windows (UIPI) blocks OLE drag and drop
/// from a normal Explorer into an administrator window, which shows the "not allowed" cursor. The
/// classic WM_DROPFILES path can be let through the message filter, so the window opts out of OLE
/// (RevokeDragDrop), accepts shell drops (DragAcceptFiles) and allows WM_DROPFILES/WM_COPYDATA/
/// WM_COPYGLOBALDATA. Trade-off: no drag-over feedback, only the final drop.
/// </summary>
internal static class ShellFileDrop
{
    private const int WmDropFiles = 0x0233;
    private const int WmCopyData = 0x004A;
    private const int WmCopyGlobalData = 0x0049;
    private const uint MsgfltAllow = 1;
    private const int GwlExStyle = -20;
    private const int WsExAcceptFiles = 0x00000010;

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    public static void Enable(Window window, Action<string[]> onDrop)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var revoke = RevokeDragDrop(hwnd); // DRAGDROP_E_NOTREGISTERED is fine
        var filters = new[] { WmDropFiles, WmCopyData, WmCopyGlobalData }
            .Select(m => ChangeWindowMessageFilterEx(hwnd, (uint)m, MsgfltAllow, IntPtr.Zero))
            .ToArray();

        DragAcceptFiles(hwnd, true);
        var accepts = (GetWindowLongW(hwnd, GwlExStyle) & WsExAcceptFiles) != 0;
        Serilog.Log.Information("Shell file drop enabled: revoke=0x{Revoke:X8}, filters={Filters}, acceptFiles={Accepts}",
            revoke, string.Join(",", filters), accepts);

        HwndSource.FromHwnd(hwnd)!.AddHook((IntPtr _, int msg, IntPtr wParam, IntPtr _, ref bool handled) =>
        {
            if (msg == WmDropFiles)
            {
                var files = ReadFiles(wParam);
                Serilog.Log.Information("WM_DROPFILES received with {Count} file(s)", files.Length);
                onDrop(files);
                handled = true;
            }

            return IntPtr.Zero;
        });
    }

    private static string[] ReadFiles(IntPtr hDrop)
    {
        try
        {
            var count = DragQueryFileW(hDrop, uint.MaxValue, null, 0);
            var files = new string[count];
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFileW(hDrop, i, null, 0);
                var buffer = new StringBuilder((int)length + 1);
                DragQueryFileW(hDrop, i, buffer, (uint)buffer.Capacity);
                files[i] = buffer.ToString();
            }

            return files;
        }
        finally
        {
            DragFinish(hDrop);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hwnd, bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(IntPtr hDrop, uint index, StringBuilder? file, uint length);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr hDrop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeFilterStruct);
}
