using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WRApperture.Client.ViewModels;

namespace WRApperture.Client.Views;

/// <summary>
/// The "Opening Chrome… Connecting" card centered on the screen when the main window cannot show it (hidden in the
/// tray or minimized). Never takes the focus and lets clicks through; closes when the step ends.
/// </summary>
public partial class ProgressToast : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;

    public ProgressToast() => InitializeComponent();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    /// <summary>Shows the card while <paramref name="vm"/> reports progress and <paramref name="main"/> is out of sight.</summary>
    public static void Follow(MainViewModel vm, Window main)
    {
        ProgressToast? toast = null;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.IsProgressVisible))
            {
                return;
            }

            if (vm.IsProgressVisible && (!main.IsVisible || main.WindowState == WindowState.Minimized))
            {
                toast ??= new ProgressToast { DataContext = vm };
                toast.Show();
            }
            else if (!vm.IsProgressVisible && toast is not null)
            {
                toast.Close();
                toast = null;
            }
        };
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
