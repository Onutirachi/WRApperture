using System.Windows;
using WRApperture.Host.Manager.Services;

namespace WRApperture.Host.Manager.Views;

public partial class TokenWindow : Window
{
    public TokenWindow(TokenInfo info)
    {
        InitializeComponent();
        AddressText.Text = info.Addresses;
        PortText.Text = info.Port;
        TokenBox.Text = info.Token;
        FingerprintBox.Text = info.Fingerprint;
        Loaded += (_, _) => { TokenBox.Focus(); TokenBox.SelectAll(); };
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TokenBox.Text);
        CopyButton.Content = "Copiado ✓";
    }
}
