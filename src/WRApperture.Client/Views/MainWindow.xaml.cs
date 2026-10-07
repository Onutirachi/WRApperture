using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WRApperture.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

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
}
