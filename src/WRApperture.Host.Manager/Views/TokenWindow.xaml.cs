using System.Windows;
using WRApperture.Host.Manager.Services;
using WRApperture.Shared.Localization;

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
        CopyButton.Content = Strings.T("manager.token.copied");
    }
}
