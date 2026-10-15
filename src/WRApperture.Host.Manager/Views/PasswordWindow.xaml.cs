using System.Windows;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Manager.Views;

/// <summary>Asks for a new password for the account signed in at this PC (typed twice).</summary>
public partial class PasswordWindow : Window
{
    public PasswordWindow(string userName)
    {
        InitializeComponent();
        Heading.Text = Strings.T("manager.password.heading", userName);
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public string? Password { get; private set; }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string? error = PasswordBox.Password.Length == 0 ? Strings.T("manager.password.missing")
            : PasswordBox.Password != ConfirmBox.Password ? Strings.T("manager.password.mismatch")
            : null;
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Password = PasswordBox.Password;
        DialogResult = true;
    }
}
