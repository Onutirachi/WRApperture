using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Updates;
using WRApperture.Shared;
using WRApperture.Shared.Localization;
using WRApperture.Shared.Updates;

namespace WRApperture.Client.ViewModels;

/// <summary>
/// Updates: the client's own (silent setup, then the installer reopens it with --tray) and the selected
/// host's (POST /api/update). One GitHub check serves both.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Soon after start, so a host left behind by an update is noticed right away.</summary>
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NoticeDelay = TimeSpan.FromSeconds(3);

    private bool _installingClient;

    /// <summary>Hosts already asked to update by themselves in this run (never twice).</summary>
    private readonly HashSet<Guid> _autoUpdatedHosts = [];

    /// <summary>A notification-area notice; the action (if any) runs when it is clicked.</summary>
    public event Action<string, Action?>? NoticeRequested;

    /// <summary>The setup was started: the app must quit so it can replace the files.</summary>
    public event Action? ExitRequested;

    /// <summary>A newer client exists and was not installed automatically: the status bar offers it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallClientUpdateCommand))]
    private bool _isClientUpdateAvailable;

    [ObservableProperty] private string _clientUpdateLabel = string.Empty;

    /// <summary>The selected host's agent is older than the latest release (shows "Update the remote PC").</summary>
    [ObservableProperty] private bool _isHostOutdated;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateHostCommand))]
    private bool _canUpdateHost;

    /// <summary>Menu toggle: install new client versions by themselves (default on).</summary>
    public bool AutoUpdate
    {
        get => _state.AutoUpdate;
        set
        {
            if (_state.AutoUpdate == value)
            {
                return;
            }

            _state = _state with { AutoUpdate = value };
            _store.Save(_state);
            OnPropertyChanged();
            if (value && _updates.IsUpdateAvailable && UpdateSource.RunsFromInstallDirectory(UpdateProduct.Client))
            {
                _ = InstallClientUpdateAsync();
            }
        }
    }

    /// <summary>Background checks: about a minute after start, then every 6 hours (runs until the app exits).</summary>
    public async void StartUpdateChecks()
    {
        await Task.Delay(FirstCheckDelay);
        while (true)
        {
            try
            {
                await CheckForUpdatesCoreAsync(manual: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Scheduled update check failed");
            }

            await Task.Delay(UpdateSource.CheckInterval);
        }
    }

    [RelayCommand]
    private Task CheckForUpdatesAsync() => CheckForUpdatesCoreAsync(manual: true);

    private async Task CheckForUpdatesCoreAsync(bool manual)
    {
        if (manual)
        {
            SetStatus(Strings.T("client.updates.checking"), warning: false);
            if (CanRefresh())
            {
                // The host's current version, not the one of the last sync.
                await SyncAsync(interactive: false);
            }
        }

        ReleaseInfo? latest;
        try
        {
            latest = await _updates.CheckAsync(manual);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // FR-002: a failed automatic check only logs.
            _logger.LogWarning("Update check failed: {Error}", ex.Message);
            if (manual)
            {
                SetStatus(Strings.T("client.updates.checkFailed", ex.Message), warning: true);
            }

            return;
        }

        IsClientUpdateAvailable = _updates.IsUpdateAvailable && !_installingClient;
        if (latest is null || !_updates.IsUpdateAvailable)
        {
            if (manual)
            {
                SetStatus(Strings.T("client.updates.upToDate", ProductVersion.Format(_updates.Current)), warning: false);
            }

            ShowHostUpdateWarning();
            return;
        }

        var version = ProductVersion.Format(latest.Version);
        ClientUpdateLabel = Strings.T("client.updates.install", version);
        // Unattended only for the installed copy: a build run from elsewhere just offers the update.
        if (AutoUpdate && UpdateSource.RunsFromInstallDirectory(UpdateProduct.Client))
        {
            await InstallClientUpdateAsync();
            return;
        }

        if (manual || _state.UpdateNoticeVersion != version)
        {
            _state = _state with { UpdateNoticeVersion = version };
            _store.Save(_state);
            SetStatus(Strings.T("client.updates.available", version, ProductVersion.Format(_updates.Current)), warning: false);
            NoticeRequested?.Invoke(Strings.T("client.updates.availableNotice", version), () => _ = InstallClientUpdateAsync());
        }
        else
        {
            ShowHostUpdateWarning();
        }
    }

    [RelayCommand(CanExecute = nameof(IsClientUpdateAvailable))]
    private Task InstallClientUpdateAsync() => InstallClientUpdateCoreAsync();

    /// <summary>Downloads and verifies, waits for any open/release in progress, then starts the silent setup and quits.</summary>
    private async Task InstallClientUpdateCoreAsync()
    {
        if (_installingClient || _updates.Latest is not { } release || !_updates.IsUpdateAvailable)
        {
            return;
        }

        _installingClient = true;
        IsClientUpdateAvailable = false;
        var version = ProductVersion.Format(release.Version);
        try
        {
            SetStatus(Strings.T("client.updates.downloading", version), warning: false);
            var setup = await _updates.DownloadAsync(release);

            // Never pull the rug from under an app being opened or a host session being released.
            while (IsProgressVisible)
            {
                await Task.Delay(BusyRetryDelay);
            }

            SetStatus(Strings.T("client.updates.installing", version), warning: false);
            NoticeRequested?.Invoke(Strings.T("client.updates.installing", version), null);
            await Task.Delay(NoticeDelay);
            _logger.LogInformation("Starting the client setup {Setup} silently and exiting", setup.Path);
            SetupLauncher.Start(setup, UpdateSource.SilentArguments).Dispose();
            ExitRequested?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UpdateVerificationException or IOException
                                       or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Client update to {Latest} failed", release.Tag);
            SetStatus(Strings.T("client.updates.failed", ex.Message), warning: true);
            _installingClient = false;
            IsClientUpdateAvailable = true;
        }
    }

    /// <summary>Asks the selected host's agent to install the latest release by itself.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateHost))]
    private async Task UpdateHostAsync()
    {
        if (SelectedHost is not { } option)
        {
            return;
        }

        var host = option.Host;
        CanUpdateHost = false; // one request at a time
        SetStatus(Strings.T("client.updates.hostRequesting", host.DisplayName), warning: false);
        try
        {
            using var agent = _agents(host);
            var result = await agent.RequestHostUpdateAsync();
            _logger.LogInformation("Update of {Address} requested: {Outcome} ({Current} → {Latest})",
                host.Address, result.Outcome, result.Current, result.Latest);
            if (result.Outcome == UpdateOutcomes.Installing)
            {
                SetStatus(Strings.T("client.updates.hostInstalling", host.DisplayName, result.Latest), warning: false);
                return; // stays disabled: the next sync shows the new version
            }

            SetStatus(Strings.T("client.updates.hostUpToDate", host.DisplayName, result.Current), warning: false);
        }
        catch (AgentException ex)
        {
            _logger.LogWarning(ex, "Update of {Address} failed", host.Address);
            SetStatus(Strings.T("client.updates.hostFailed", host.DisplayName, ex.Message), warning: true);
        }

        EvaluateHostUpdate();
    }

    /// <summary>Requests the update only when the host's own setting allows unattended updates.</summary>
    private async Task AutoUpdateHostAsync(Core.Model.PairedHost host)
    {
        try
        {
            using var agent = _agents(host);
            var status = await agent.GetHostUpdateStatusAsync();
            if (!status.AutoUpdate)
            {
                _logger.LogInformation("{Address} is outdated but its automatic updates are off; not updating it", host.Address);
                ShowManualHostUpdateWarning();
                return;
            }
        }
        catch (AgentException ex)
        {
            _logger.LogWarning(ex, "Could not read the update status of {Address}", host.Address);
            ShowManualHostUpdateWarning();
            return;
        }

        if (SelectedHost?.Host.Id == host.Id && CanUpdateHost)
        {
            await UpdateHostAsync();
        }
    }

    private void ShowManualHostUpdateWarning()
    {
        if (SelectedHost is not { } option || _updates.Latest is not { } latest || !IsHostOutdated)
        {
            return;
        }

        var (name, version, newest) = (option.Host.DisplayName, option.Host.AgentVersion, ProductVersion.Format(latest.Version));
        SetStatus(CanUpdateHost
            ? Strings.T("client.updates.hostOutdated", name, version, newest)
            : Strings.T("client.updates.hostOutdatedManual", name, version, newest), warning: true);
    }

    /// <summary>Compares the selected host's agent (as of its last sync) with the latest release.</summary>
    private void EvaluateHostUpdate()
    {
        var state = SelectedHost is { } option ? _updates.HostState(option.Host) : HostUpdateState.Current;
        IsHostOutdated = state != HostUpdateState.Current;
        CanUpdateHost = state == HostUpdateState.CanUpdate;
    }

    /// <summary>
    /// After a sync or a check: when the selected host is outdated, asks it to update by itself if both ends have
    /// automatic updates on, otherwise warns in the status bar.
    /// </summary>
    private void ShowHostUpdateWarning()
    {
        EvaluateHostUpdate();
        if (!IsHostOutdated || SelectedHost is not { } option || _updates.Latest is not { } latest)
        {
            return;
        }

        if (CanUpdateHost && AutoUpdate && _autoUpdatedHosts.Add(option.Host.Id))
        {
            _ = AutoUpdateHostAsync(option.Host);
            return;
        }

        ShowManualHostUpdateWarning();
    }
}
