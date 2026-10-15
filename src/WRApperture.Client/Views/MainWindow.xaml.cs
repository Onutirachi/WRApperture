using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WRApperture.Client.ViewModels;
using WRApperture.Ui;

namespace WRApperture.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ThemeMenu.AddTo(ThemeItem);
        LanguageItem.Click += (_, _) => LanguageWindow.Choose(this, ((App)Application.Current).Restart);
        Activated += (_, _) => Vm.OnWindowActivated();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5 && Vm.RefreshCommand.CanExecute(null))
            {
                e.Handled = true;
                Vm.RefreshCommand.Execute(null);
            }
        };
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private bool _exiting;

    /// <summary>The window was hidden to the tray instead of closing.</summary>
    public event Action? HiddenToTray;

    /// <summary>Really closes (tray "Exit"), bypassing close-to-tray.</summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && Vm.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }

        base.OnClosing(e);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.DataContext = DataContext;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnAppDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is ListBoxItem { DataContext: AppTileViewModel tile })
        {
            e.Handled = true;
            Vm.OpenAppCommand.Execute(tile);
        }
    }

    /// <summary>Right click selects the app first, so the menu and F2 act on what the user sees highlighted.</summary>
    private void OnAppRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void OnAppsKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm.SelectedApp is not { } tile)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                Vm.OpenAppCommand.Execute(tile);
                break;
            case Key.F2:
                e.Handled = true;
                Vm.RenameAppCommand.Execute(tile);
                break;
        }
    }
}
