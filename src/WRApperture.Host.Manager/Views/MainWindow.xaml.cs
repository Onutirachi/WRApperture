using System.Windows;
using System.Windows.Input;
using WRApperture.Host.Manager.ViewModels;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Manager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // The window usually runs elevated, where OLE drag and drop from Explorer is blocked: use shell drops.
        SourceInitialized += (_, _) => ShellFileDrop.Enable(this, OnFilesDropped);
        // Alternative to dragging: Ctrl+C on shortcuts/programs in Explorer, Ctrl+V here.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, OnPaste, (_, e) => e.CanExecute = Clipboard.ContainsFileDropList()));
        Loaded += (_, _) => Vm.CheckUpdatesCommand.Execute(null);

        // Approvals happen in another window (process): pick up new or removed devices while this one is open.
        var devices = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        devices.Tick += (_, _) => Vm.RefreshDevices();
        Loaded += (_, _) => devices.Start();
        Closed += (_, _) => devices.Stop();

        // No refresh button: coming back to the window re-reads everything (apps, This PC, agent); F5 forces it.
        var lastRefresh = DateTime.UtcNow;
        Activated += (_, _) =>
        {
            if (DateTime.UtcNow - lastRefresh > TimeSpan.FromSeconds(5))
            {
                lastRefresh = DateTime.UtcNow;
                Vm.RefreshCommand.Execute(null);
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                lastRefresh = DateTime.UtcNow;
                Vm.RefreshCommand.Execute(null);
            }
        };
    }

    private void OnPaste(object sender, ExecutedRoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return; // let text boxes paste text normally
        }

        var files = Clipboard.GetFileDropList();
        var paths = new string[files.Count];
        files.CopyTo(paths, 0);
        OnFilesDropped(paths);
    }

    private ManagerViewModel Vm => (ManagerViewModel)DataContext;

    private void OnFilesDropped(string[] files)
    {
        if (files.Length == 0)
        {
            return;
        }

        if (!Vm.AddFilesCommand.CanExecute(files))
        {
            MessageBox.Show(this, Strings.T("manager.main.adminRequired"), "WRApperture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Activate();
        // Let Explorer finish the drop before showing modal dialogs.
        Dispatcher.BeginInvoke(() => Vm.AddFilesCommand.Execute(files));
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        if (ThemeButton.ContextMenu is null)
        {
            ThemeButton.ContextMenu = new System.Windows.Controls.ContextMenu();
            WRApperture.Ui.ThemeMenu.AddTo(ThemeButton.ContextMenu);
            ThemeButton.ContextMenu.Items.Add(new System.Windows.Controls.Separator());
            var language = new System.Windows.Controls.MenuItem { Header = Strings.T("common.language.menu") };
            language.Click += (_, _) => WRApperture.Ui.LanguageWindow.Choose(this, Restart);
            ThemeButton.ContextMenu.Items.Add(language);
        }

        ThemeButton.ContextMenu.PlacementTarget = ThemeButton;
        ThemeButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ThemeButton.ContextMenu.IsOpen = true;
    }

    /// <summary>Same arguments, same elevation: a process started by an elevated one is elevated too.</summary>
    private static void Restart()
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        System.Diagnostics.Process.Start(start);
        Application.Current.Shutdown();
    }

    private void OnAppDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.EditCommand.CanExecute(null))
        {
            Vm.EditCommand.Execute(null);
        }
    }
}
