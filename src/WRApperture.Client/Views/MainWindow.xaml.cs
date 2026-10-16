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

        AddColorItems();
        Activated += (_, _) => Vm.OnWindowActivated();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5 && Vm.RefreshCommand.CanExecute(null))
            {
                e.Handled = true;
                Vm.RefreshCommand.Execute(null);
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                Search.Focus();
                Search.SelectAll();
            }
        };
        // The settings page shows the current theme each time it opens (the tray or Windows may have changed it).
        DataContextChanged += (_, _) => Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen) && Vm.IsSettingsOpen)
            {
                SyncThemeChoice();
            }
        };
        SyncThemeChoice();
    }

    private ContextMenu HostMenu => (ContextMenu)FindResource("HostMenu");

    /// <summary>"Color" submenu: no color, then the palette, each with its swatch.</summary>
    private void AddColorItems()
    {
        var color = HostMenu.Items.OfType<MenuItem>().First(m => Equals(m.Tag, "color"));
        color.Items.Add(new MenuItem { Header = WRApperture.Shared.Localization.Strings.T("client.color.none") });
        foreach (var (key, hex) in HostOption.Palette)
        {
            color.Items.Add(new MenuItem
            {
                Header = HostOption.ColorName(key),
                Tag = key,
                Icon = new System.Windows.Shapes.Rectangle
                {
                    Width = 12, Height = 12, RadiusX = 2, RadiusY = 2,
                    Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)),
                },
            });
        }

        foreach (var item in color.Items.OfType<MenuItem>())
        {
            item.Click += (_, _) => Vm.SetHostColorCommand.Execute(item.Tag as string);
        }
    }

    /// <summary>A card's ○○○: selects that host, then opens its menu under the button.</summary>
    private void OnHostMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HostOption host } button)
        {
            return;
        }

        Vm.SelectedHost = host;
        var menu = HostMenu;
        menu.DataContext = DataContext;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private bool _syncingTheme;

    /// <summary>The settings page's theme choice follows the current preference.</summary>
    private void SyncThemeChoice()
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

    private void OnLanguageClick(object sender, RoutedEventArgs e) =>
        LanguageWindow.Choose(this, ((App)Application.Current).Restart);

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

    /// <summary>A narrow window keeps the search box usable: the labeled buttons show only their icons (names stay in the tooltips).</summary>
    private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 620;
        DesktopLabel.Visibility = ManageLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
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
