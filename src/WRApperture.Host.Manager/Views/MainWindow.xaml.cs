using System.Windows;
using System.Windows.Input;
using WRApperture.Host.Manager.ViewModels;

namespace WRApperture.Host.Manager.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private ManagerViewModel Vm => (ManagerViewModel)DataContext;

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (HasFiles(e) && Vm.IsAdmin)
        {
            DropHighlight.Visibility = Visibility.Visible;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFiles(e) && Vm.IsAdmin ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropHighlight.Visibility = Visibility.Collapsed;

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && Vm.AddFilesCommand.CanExecute(files))
        {
            // Let the drag source (Explorer) finish before showing modal dialogs.
            Dispatcher.BeginInvoke(() => Vm.AddFilesCommand.Execute(files));
        }
    }

    private void OnAppDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.EditCommand.CanExecute(null))
        {
            Vm.EditCommand.Execute(null);
        }
    }
}
