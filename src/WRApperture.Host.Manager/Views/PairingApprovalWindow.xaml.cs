using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using WRApperture.Host.Manager.Services;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Manager.Views;

/// <summary>
/// Opened by the agent on the screen of whoever uses this PC ("--pairing &lt;id&gt; &lt;secret&gt;"): shows who asks
/// to pair and the verification code, and sends Allow or Deny back to the agent. Closing it denies.
/// </summary>
public partial class PairingApprovalWindow : Window
{
    public const string Argument = "--pairing";

    private readonly PairingApprovalClient _client;
    private readonly string _id;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _expiresAt;
    private bool _answered;

    internal PairingApprovalWindow(PairingApprovalClient client, string id)
    {
        InitializeComponent();
        _client = client;
        _id = id;
        _timer.Tick += (_, _) => UpdateCountdown();
        Loaded += async (_, _) => await LoadAsync();
        Closing += OnClosing;
    }

    private async Task LoadAsync()
    {
        Activate();
        try
        {
            var details = await _client.GetDetailsAsync(_id);
            if (details is null)
            {
                ShowMessage(Strings.T("manager.approval.gone"));
                return;
            }

            ClientText.Text = details.ClientName;
            OriginText.Text = string.IsNullOrEmpty(details.ClientUser)
                ? Strings.T("manager.approval.origin", details.Address)
                : Strings.T("manager.approval.originUser", details.ClientUser, details.Address);
            CodeText.Text = details.Code;
            _expiresAt = DateTime.UtcNow.AddSeconds(details.ExpiresInSeconds);
            MessageText.Visibility = Visibility.Collapsed;
            DetailsPanel.Visibility = Visibility.Visible;
            AllowButton.IsEnabled = true;
            UpdateCountdown();
            _timer.Start();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Error(ex, "Pairing request {Id}: agent unreachable", _id);
            ShowMessage(Strings.T("manager.approval.agentUnreachable"));
        }
    }

    private void UpdateCountdown()
    {
        var left = _expiresAt - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            _timer.Stop();
            ShowMessage(Strings.T("manager.approval.expired"));
            return;
        }

        ExpiresText.Text = Strings.T("manager.approval.expiresIn", $"{(int)left.TotalMinutes}:{left.Seconds:00}");
    }

    private async void OnAllow(object sender, RoutedEventArgs e) => await AnswerAsync(approve: true);

    private async void OnDeny(object sender, RoutedEventArgs e) => await AnswerAsync(approve: false);

    private async Task AnswerAsync(bool approve)
    {
        if (_answered || DetailsPanel.Visibility != Visibility.Visible)
        {
            _answered = true;
            Close();
            return;
        }

        _answered = true;
        AllowButton.IsEnabled = DenyButton.IsEnabled = false;
        try
        {
            if (!await _client.DecideAsync(_id, approve))
            {
                ShowMessage(Strings.T("manager.approval.gone"));
                return;
            }

            Log.Information("Pairing request {Id} {Decision} by the user", _id, approve ? "allowed" : "denied");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Error(ex, "Pairing request {Id}: could not send the decision", _id);
            ShowMessage(Strings.T("manager.approval.agentUnreachable"));
            return;
        }

        Close();
    }

    /// <summary>Closing without answering denies, so the client is not left waiting until it expires.</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _timer.Stop();
        if (_answered || DetailsPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        _answered = true;
        try
        {
            _client.DecideAsync(_id, approve: false).Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException ex)
        {
            Log.Warning(ex, "Pairing request {Id}: could not deny on close", _id);
        }
    }

    /// <summary>Replaces the details with a message; the only button left closes the window.</summary>
    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.Visibility = Visibility.Visible;
        DetailsPanel.Visibility = Visibility.Collapsed;
        AllowButton.Visibility = Visibility.Collapsed;
        DenyButton.IsEnabled = true;
        DenyButton.Content = Strings.T("manager.approval.close");
    }
}
