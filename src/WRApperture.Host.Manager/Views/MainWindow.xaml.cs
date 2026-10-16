using System.Windows;
using System.Windows.Input;
using WRApperture.Host.Manager.ViewModels;
using WRApperture.Shared.Localization;
using WRApperture.Ui;

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

    private bool _syncingTheme;

    /// <summary>The settings page's theme choice follows the current preference.</summary>
    private void OnSettingsShown(object sender, RoutedEventArgs e)
    {
        _syncingTheme = true;
        (ThemeManager.Preference switch
        {
            ThemePreference.Light => ThemeLight,
            ThemePreference.Dark => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;
        _syncingTheme = false;
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingTheme)
        {
            return;
        }

        ThemeManager.SetPreference(sender == ThemeLight ? ThemePreference.Light : sender == ThemeDark ? ThemePreference.Dark : ThemePreference.System);
    }

    private void OnLanguageClick(object sender, RoutedEventArgs e) => LanguageWindow.Choose(this, Restart);

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
