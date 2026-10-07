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
        }

        ThemeButton.ContextMenu.PlacementTarget = ThemeButton;
        ThemeButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ThemeButton.ContextMenu.IsOpen = true;
    }

    private void OnAppDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.EditCommand.CanExecute(null))
        {
            Vm.EditCommand.Execute(null);
        }
    }
}
