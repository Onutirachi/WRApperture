using System.Windows;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Discovery;
using WRApperture.Client.Views;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Services;

public sealed record AddHostResult(string Address, int AgentPort, string Token, PairingResult Pairing);

public sealed record CredentialsInput(string UserName, string Password);

/// <param name="MicrosoftAccount">The host signs in with a Microsoft account: e-mail and account password, not the PIN.</param>
/// <param name="FirstTime">Right after adding the host: explains why it is asked, and Cancel reads "Not now".</param>
public sealed record CredentialsContext(bool MicrosoftAccount, bool FirstTime);

public sealed record ReleaseDecision(bool Always);

public interface IDialogService
{
    /// <param name="knownFingerprints">Hosts already added (marked as such in the list of PCs found).</param>
    AddHostResult? AddHost(string? address = null, int? agentPort = null, IReadOnlyCollection<string>? knownFingerprints = null);
    CredentialsInput? AskCredentials(string hostName, string? userName, CredentialsContext? context = null);

    /// <summary>Null when the user cancels.</summary>
    ReleaseDecision? AskReleaseSession(string hostName, IReadOnlyList<BusySessionDto> sessions);

    /// <summary>Local name for an app; null when the user cancels.</summary>
    string? AskAppName(string currentName, string hostName);

    /// <summary>Local name for a host; null when the user cancels, blank or the computer's name to use the computer's name.</summary>
    string? AskHostName(string currentName, string computerName);

    bool Confirm(string title, string message);
    void Error(string message);
    void Info(string message);
}

internal sealed class DialogService(IHostDiscovery discovery) : IDialogService
{
    private const string Title = "WRApperture";

    public AddHostResult? AddHost(string? address = null, int? agentPort = null, IReadOnlyCollection<string>? knownFingerprints = null)
    {
        var window = Owned(new AddHostWindow(discovery, address, agentPort ?? ApiContract.DefaultPort, knownFingerprints ?? []));
        return window.ShowDialog() == true ? window.Result : null;
    }

    public CredentialsInput? AskCredentials(string hostName, string? userName, CredentialsContext? context = null)
    {
        var window = Owned(new CredentialsWindow(hostName, userName, context));
        return window.ShowDialog() == true ? window.Result : null;
    }

    public ReleaseDecision? AskReleaseSession(string hostName, IReadOnlyList<BusySessionDto> sessions)
    {
        var window = Owned(new ReleaseSessionWindow(hostName, sessions));
        return window.ShowDialog() == true ? new ReleaseDecision(window.Always) : null;
    }

    public string? AskAppName(string currentName, string hostName)
    {
        var window = Owned(new RenameAppWindow(currentName, hostName));
        return window.ShowDialog() == true ? window.Result : null;
    }

    public string? AskHostName(string currentName, string computerName)
    {
        var window = Owned(new RenameAppWindow(currentName, computerName, Strings.T("client.rename.hostTitle"),
            Strings.T("client.rename.computerName", computerName)));
        return window.ShowDialog() == true ? window.Result : null;
    }

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public void Error(string message) =>
        Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void Info(string message) =>
        Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>
    /// Opened from the tray, the main window may be hidden (or never shown): then the dialog stands on its
    /// own, centered and in the taskbar, instead of being owned by an invisible window.
    /// </summary>
    private static T Owned<T>(T window) where T : Window
    {
        if (Application.Current.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowInTaskbar = true;
        }

        return window;
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Application.Current.MainWindow is { IsVisible: true } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
