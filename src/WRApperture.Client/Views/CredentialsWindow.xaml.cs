using System.Windows;
using WRApperture.Client.Services;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Views;

public partial class CredentialsWindow : Window
{
    public CredentialsWindow(string hostName, string? userName, CredentialsContext? context = null)
    {
        InitializeComponent();
        UserBox.Text = userName ?? string.Empty;
        Heading.Text = context?.FirstTime == true
            ? Strings.T("client.credentials.firstHeading", hostName)
            : Strings.T("client.credentials.heading", hostName);
        if (context?.FirstTime == true)
        {
            IntroText.Visibility = Visibility.Visible;
            CancelButton.Content = Strings.T("client.credentials.later");
        }

        if (context?.MicrosoftAccount == true)
        {
            MicrosoftHint.Visibility = Visibility.Visible;
            UserHint.Visibility = Visibility.Collapsed;
        }

        Loaded += (_, _) => (string.IsNullOrEmpty(userName) ? (UIElement)UserBox : PasswordBox).Focus();
    }

    public CredentialsInput? Result { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserBox.Text) || PasswordBox.Password.Length == 0)
        {
            ErrorText.Text = Strings.T("client.credentials.missing");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Result = new CredentialsInput(UserBox.Text.Trim(), PasswordBox.Password);
        DialogResult = true;
    }
}
