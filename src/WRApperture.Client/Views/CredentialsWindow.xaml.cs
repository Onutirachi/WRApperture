using System.Windows;
using WRApperture.Client.Services;

namespace WRApperture.Client.Views;

public partial class CredentialsWindow : Window
{
    public CredentialsWindow(string hostName, string? userName)
    {
        InitializeComponent();
        Heading.Text = $"Salvar login de {hostName}";
        UserBox.Text = userName ?? string.Empty;
        Loaded += (_, _) => (string.IsNullOrEmpty(userName) ? (UIElement)UserBox : PasswordBox).Focus();
    }

    public CredentialsInput? Result { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserBox.Text) || PasswordBox.Password.Length == 0)
        {
            ErrorText.Text = "Informe usuário e senha.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Result = new CredentialsInput(UserBox.Text.Trim(), PasswordBox.Password);
        DialogResult = true;
    }
}
