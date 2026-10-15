using System.Globalization;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Discovery;
using WRApperture.Client.Services;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Views;

/// <summary>
/// Adding a host: pick one found on the network and wait for the person at it to allow (the default), or type its
/// address and a token, then confirm its fingerprint (manual, also for re-pairing an existing host).
/// </summary>
public partial class AddHostWindow : Window
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(2);

    private enum Mode
    {
        Find,
        Wait,
        Manual,
        Confirm,
    }

    private readonly IHostDiscovery _discovery;
    private readonly CancellationTokenSource _closing = new();
    private Mode _mode;
    private bool _busy;
    private PairingResult? _pairing;
    private string _address = string.Empty;
    private int _port;

    public AddHostWindow(IHostDiscovery discovery, string? address, int port)
    {
        InitializeComponent();
        _discovery = discovery;
        AddressBox.Text = address ?? string.Empty;
        PortBox.Text = port.ToString(CultureInfo.InvariantCulture);
        Closed += (_, _) => _closing.Cancel();

        if (string.IsNullOrEmpty(address))
        {
            Show(Mode.Find);
            Loaded += async (_, _) => await SearchAsync();
        }
        else
        {
            Show(Mode.Manual);
            Loaded += (_, _) => TokenBox.Focus();
        }
    }

    public AddHostResult? Result { get; private set; }

    private DiscoveredHost? SelectedFound => FoundList.SelectedItem as DiscoveredHost;

    private void Show(Mode mode)
    {
        _mode = mode;
        FindStep.Visibility = mode == Mode.Find ? Visibility.Visible : Visibility.Collapsed;
        WaitStep.Visibility = mode == Mode.Wait ? Visibility.Visible : Visibility.Collapsed;
        Step1.Visibility = mode == Mode.Manual ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = mode == Mode.Confirm ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Visibility = mode == Mode.Wait ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = mode == Mode.Confirm ? Strings.T("client.addHost.confirmAndSave") : Strings.T("client.addHost.connect");
        ErrorText.Visibility = Visibility.Collapsed;
        UpdatePrimary();
    }

    private void UpdatePrimary() =>
        PrimaryButton.IsEnabled = !_busy && (_mode != Mode.Find || SelectedFound is not null);

    private async Task SearchAsync()
    {
        SearchButton.IsEnabled = false;
        FoundList.ItemsSource = null;
        FindStatus.Text = Strings.T("client.addHost.searching");
        FindStatus.Visibility = Visibility.Visible;
        try
        {
            var hosts = await _discovery.ScanAsync(ScanDuration, _closing.Token);
            FoundList.ItemsSource = hosts;
            FindStatus.Visibility = hosts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FindStatus.Text = Strings.T("client.addHost.noneFound");
            if (hosts.Count == 1)
            {
                FoundList.SelectedIndex = 0;
                FoundList.Focus();
            }
        }
        catch (OperationCanceledException)
        {
            return; // window closed
        }
        catch (SocketException)
        {
            FindStatus.Text = Strings.T("client.addHost.noneFound");
        }
        finally
        {
            SearchButton.IsEnabled = true;
            UpdatePrimary();
        }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e) => await SearchAsync();

    private void OnManualClick(object sender, RoutedEventArgs e)
    {
        Show(Mode.Manual);
        AddressBox.Focus();
    }

    private void OnFoundSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePrimary();

    private async void OnFoundDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedFound is { } host && !_busy)
        {
            await PairByApprovalAsync(host);
        }
    }

    private async void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        switch (_mode)
        {
            case Mode.Find when SelectedFound is { } host:
                await PairByApprovalAsync(host);
                break;
            case Mode.Manual:
                await ConnectManuallyAsync();
                break;
            case Mode.Confirm:
                Result = new AddHostResult(_address, _port, TokenBox.Password.Trim(), _pairing!);
                DialogResult = true;
                break;
        }
    }

    private async Task PairByApprovalAsync(DiscoveredHost host)
    {
        if (!host.SupportsPairing)
        {
            // Agents before pairing by approval: the token way, address already filled in.
            AddressBox.Text = host.Address;
            PortBox.Text = host.AgentPort.ToString(CultureInfo.InvariantCulture);
            Show(Mode.Manual);
            ShowError(Strings.T("client.addHost.needsToken", host.HostName));
            TokenBox.Focus();
            return;
        }

        SetBusy(true);
        try
        {
            var address = await _discovery.ChooseAddressAsync(host, _closing.Token);
            var pending = await AgentClient.RequestPairingAsync(address, host.AgentPort, Environment.MachineName, Environment.UserName,
                _closing.Token);
            WaitHeading.Text = Strings.T("client.addHost.waitingHeading", host.HostName);
            WaitHint.Text = Strings.T("client.addHost.waitingHint", host.HostName);
            CodeText.Text = pending.Code;
            Show(Mode.Wait);

            var token = await AgentClient.WaitForApprovalAsync(pending, _closing.Token);
            var pairing = await AgentClient.CompleteApprovedPairingAsync(pending, token, _closing.Token);
            Result = new AddHostResult(address, host.AgentPort, token, pairing);
            DialogResult = true;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Window closed while waiting.
        }
        catch (AgentException ex)
        {
            Show(Mode.Find);
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ConnectManuallyAsync()
    {
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
            _pairing = await AgentClient.PairAsync(address, port, TokenBox.Password.Trim(), _closing.Token);
            _address = address;
            _port = port;
            HostNameText.Text = Strings.T("client.addHost.hostLine", _pairing.Host.HostName, _pairing.Host.AgentVersion);
            FingerprintText.Text = CertificateFingerprint.Format(_pairing.ObservedFingerprint);
            Show(Mode.Confirm);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
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
        _busy = busy;
        if (_mode == Mode.Manual)
        {
            PrimaryButton.Content = busy ? Strings.T("client.addHost.connecting") : Strings.T("client.addHost.connect");
        }

        UpdatePrimary();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
