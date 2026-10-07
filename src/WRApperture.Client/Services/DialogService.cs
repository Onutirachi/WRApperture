using System.Windows;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Views;
using WRApperture.Shared;

namespace WRApperture.Client.Services;

public sealed record AddHostResult(string Address, int AgentPort, string Token, PairingResult Pairing);

public sealed record CredentialsInput(string UserName, string Password);

public sealed record ReleaseDecision(bool Always);

public interface IDialogService
{
    AddHostResult? AddHost(string? address = null, int? agentPort = null);
    CredentialsInput? AskCredentials(string hostName, string? userName);

    /// <summary>Null when the user cancels.</summary>
    ReleaseDecision? AskReleaseSession(string hostName, IReadOnlyList<BusySessionDto> sessions);

    bool Confirm(string title, string message);
    void Error(string message);
    void Info(string message);
}

internal sealed class DialogService : IDialogService
{
    private const string Title = "WRApperture";

    public AddHostResult? AddHost(string? address = null, int? agentPort = null)
    {
        var window = new AddHostWindow(address, agentPort ?? ApiContract.DefaultPort) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Result : null;
    }

    public CredentialsInput? AskCredentials(string hostName, string? userName)
    {
        var window = new CredentialsWindow(hostName, userName) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Result : null;
    }

    public ReleaseDecision? AskReleaseSession(string hostName, IReadOnlyList<BusySessionDto> sessions)
    {
        var window = new ReleaseSessionWindow(hostName, sessions) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? new ReleaseDecision(window.Always) : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public void Error(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, Title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void Info(string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
}
