using System.Windows;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Views;

public partial class RenameAppWindow : Window
{
    private readonly string _hostName;

    /// <param name="hostName">The name the host gives it (restored by "Use the original name").</param>
    /// <param name="title">Heading for renaming something else than an app (a host).</param>
    /// <param name="originalText">Line telling the original name, for <paramref name="title"/>.</param>
    public RenameAppWindow(string currentName, string hostName, string? title = null, string? originalText = null)
    {
        InitializeComponent();
        _hostName = hostName;
        NameBox.Text = currentName;
        HostNameText.Text = originalText ?? Strings.T("client.rename.hostName", hostName);
        if (title is not null)
        {
            Title = title;
            Heading.Text = title;
        }

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
