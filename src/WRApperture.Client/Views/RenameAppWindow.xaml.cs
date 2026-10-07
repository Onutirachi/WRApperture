using System.Windows;

namespace WRApperture.Client.Views;

public partial class RenameAppWindow : Window
{
    private readonly string _hostName;

    public RenameAppWindow(string currentName, string hostName)
    {
        InitializeComponent();
        _hostName = hostName;
        NameBox.Text = currentName;
        HostNameText.Text = $"Nome no PC remoto: {hostName}";
        RestoreButton.IsEnabled = currentName != hostName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>The chosen name; blank or equal to the host's means "use the host's name".</summary>
    public string? Result { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Result = NameBox.Text;
        DialogResult = true;
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        Result = _hostName;
        DialogResult = true;
    }
}
