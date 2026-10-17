using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Model;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.ViewModels;

/// <summary>
/// Acting on the host from here (agents that announce <see cref="AgentFeatures.RemoteControl"/>): its own app
/// ("Manage host"), closing or ending an open app, and ending the RemoteApp session (every remote app closes).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The host offers its own app as a RemoteApp.</summary>
    public bool CanManageHost => SelectedHost?.Host.ManagerAlias is not null;

    /// <summary>Open apps can be closed and the remote session ended.</summary>
    public bool CanControlHost => SelectedHost?.Host.Supports(AgentFeatures.RemoteControl) == true;

    private void NotifyHostCapabilities()
    {
        OnPropertyChanged(nameof(CanManageHost));
        OnPropertyChanged(nameof(CanControlHost));
    }

    [RelayCommand]
    private Task ManageHostAsync() => SelectedHost?.Host.ManagerAlias is { } alias
        ? OpenAppAsync(AppTileViewModel.Manager(alias))
        : Task.CompletedTask;

    [RelayCommand]
    private Task EndSessionAsync() => EndSessionOfAsync(SelectedHost);

    /// <summary>Logs off this computer's RemoteApp session on the host, after asking (also from the tray).</summary>
    public async Task EndSessionOfAsync(HostOption? option)
    {
        if (option is null || !option.Host.Supports(AgentFeatures.RemoteControl) ||
            !_dialogs.Confirm(Strings.T("client.endSession.title"), Strings.T("client.endSession.confirm", option.Label)))
        {
            return;
        }

        try
        {
            using var agent = _agents(option.Host);
            var ended = await agent.EndSessionAsync(option.Host.RdpUserName);
            SetStatus(Strings.T(ended > 0 ? "client.endSession.done" : "client.endSession.none", option.Label), warning: false);
        }
        catch (AgentException ex)
        {
            _logger.LogWarning(ex, "Ending the session on {Address} failed", option.Host.Address);
            SetStatus(Strings.T("client.endSession.failed", ex.Message), warning: true);
        }
    }

    /// <summary>Like the app's X on the host: it may still ask to save.</summary>
    [RelayCommand]
    private Task CloseAppAsync(AppTileViewModel? tile) => StopAppAsync(tile, force: false);

    /// <summary>Like "End task": nothing is saved, so it asks first.</summary>
    [RelayCommand]
    private Task KillAppAsync(AppTileViewModel? tile) =>
        tile is not null && _dialogs.Confirm(Strings.T("client.tile.killTitle"), Strings.T("client.tile.killConfirm", tile.DisplayName))
            ? StopAppAsync(tile, force: true)
            : Task.CompletedTask;

    private async Task StopAppAsync(AppTileViewModel? tile, bool force)
    {
        if (tile is null || tile.IsDesktop || SelectedHost is not { } option || !CanControlHost)
        {
            return;
        }

        try
        {
            using var agent = _agents(option.Host);
            var processes = await agent.StopAppAsync(tile.App.Alias, force);
            SetStatus(Strings.T(processes == 0 ? "client.tile.notRunning" : force ? "client.tile.killed" : "client.tile.closing", tile.DisplayName),
                warning: false);
        }
        catch (AgentException ex)
        {
            _logger.LogWarning(ex, "Stopping {Alias} on {Address} failed", tile.App.Alias, option.Host.Address);
            SetStatus(Strings.T("client.tile.stopFailed", tile.DisplayName, ex.Message), warning: true);
        }
    }
}
