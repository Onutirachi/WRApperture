using System.Globalization;
using System.Windows;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Services;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Views;

/// <summary>Two-step pairing: connect with address + token, then confirm the host fingerprint.</summary>
public partial class AddHostWindow : Window
{
    private PairingResult? _pairing;
    private string _address = string.Empty;
    private int _port;

    public AddHostWindow(string? address, int port)
    {
        InitializeComponent();
        AddressBox.Text = address ?? string.Empty;
        PortBox.Text = port.ToString(CultureInfo.InvariantCulture);
        Loaded += (_, _) => (string.IsNullOrEmpty(address) ? (UIElement)AddressBox : TokenBox).Focus();
    }

    public AddHostResult? Result { get; private set; }

    private async void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        if (_pairing is not null)
        {
            Result = new AddHostResult(_address, _port, TokenBox.Password, _pairing);
            DialogResult = true;
            return;
        }

        var address = AddressBox.Text.Trim();
        if (Uri.CheckHostName(address) == UriHostNameType.Unknown)
        {
            ShowError(Strings.T("client.addHost.invalidAddress"));
            return;
        }

        if (!int.TryParse(PortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            ShowError(Strings.T("client.addHost.invalidPort", ApiContract.DefaultPort));
            return;
        }

        if (string.IsNullOrWhiteSpace(TokenBox.Password))
        {
            ShowError(Strings.T("client.addHost.missingToken"));
            return;
        }

        SetBusy(true);
        try
        {
            _pairing = await AgentClient.PairAsync(address, port, TokenBox.Password.Trim());
            _address = address;
            _port = port;
            HostNameText.Text = Strings.T("client.addHost.hostLine", _pairing.Host.HostName, _pairing.Host.AgentVersion);
            FingerprintText.Text = CertificateFingerprint.Format(_pairing.ObservedFingerprint);
            Step1.Visibility = Visibility.Collapsed;
            Step2.Visibility = Visibility.Visible;
            ErrorText.Visibility = Visibility.Collapsed;
            PrimaryButton.Content = Strings.T("client.addHost.confirmAndSave");
        }
        catch (AgentException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        PrimaryButton.IsEnabled = !busy;
        if (_pairing is null)
        {
            PrimaryButton.Content = busy ? Strings.T("client.addHost.connecting") : Strings.T("client.addHost.connect");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
