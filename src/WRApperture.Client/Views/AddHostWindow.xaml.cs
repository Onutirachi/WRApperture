using System.Globalization;
using System.Windows;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Services;
using WRApperture.Shared;

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
            ShowError("Informe um nome de computador ou endereço IP válido.");
            return;
        }

        if (!int.TryParse(PortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            ShowError("Informe uma porta válida (padrão 5080).");
            return;
        }

        if (string.IsNullOrWhiteSpace(TokenBox.Password))
        {
            ShowError("Informe o token de pareamento.");
            return;
        }

        SetBusy(true);
        try
        {
            _pairing = await AgentClient.PairAsync(address, port, TokenBox.Password.Trim());
            _address = address;
            _port = port;
            HostNameText.Text = $"{_pairing.Host.HostName}  ·  agente {_pairing.Host.AgentVersion}";
            FingerprintText.Text = CertificateFingerprint.Format(_pairing.ObservedFingerprint);
            Step1.Visibility = Visibility.Collapsed;
            Step2.Visibility = Visibility.Visible;
            ErrorText.Visibility = Visibility.Collapsed;
            PrimaryButton.Content = "Confirmar e salvar";
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
            PrimaryButton.Content = busy ? "Conectando…" : "Conectar";
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
