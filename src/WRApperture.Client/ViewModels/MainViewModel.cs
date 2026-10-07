using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WRApperture.Client.Core;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Credentials;
using WRApperture.Client.Core.Launch;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Shortcuts;
using WRApperture.Client.Core.Signing;
using WRApperture.Client.Core.Storage;
using WRApperture.Client.Services;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.ViewModels;

/// <summary>Combo item; updated in place so the ComboBox selection never flickers.</summary>
public sealed partial class HostOption(PairedHost host) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private PairedHost _host = host;

    public string Label => string.Equals(Host.DisplayName, Host.Address, StringComparison.OrdinalIgnoreCase)
        ? Host.DisplayName
        : $"{Host.DisplayName} ({Host.Address})";
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly HostStore _store;
    private readonly IconStore _icons;
    private readonly RemoteAppLauncher _launcher;
    private readonly HostSync _sync;
    private readonly ICredentialStore _credentials;
    private readonly IDialogService _dialogs;
    private readonly IRdpSigner _signer;
    private readonly ISigningSetup _signingSetup;
    private readonly Func<PairedHost, IAgentApi> _agents;
    private readonly ILogger<MainViewModel> _logger;
    private ClientState _state;

    public MainViewModel(HostStore store, IconStore icons, RemoteAppLauncher launcher, HostSync sync,
        ICredentialStore credentials, IDialogService dialogs, IRdpSigner signer, ISigningSetup signingSetup,
        Func<PairedHost, IAgentApi> agents,
        ILogger<MainViewModel> logger)
    {
        _store = store;
        _icons = icons;
        _launcher = launcher;
        _sync = sync;
        _credentials = credentials;
        _dialogs = dialogs;
        _signer = signer;
        _signingSetup = signingSetup;
        _agents = agents;
        _logger = logger;
        _state = store.Load();

        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = o => o is AppTileViewModel t &&
            (string.IsNullOrWhiteSpace(SearchText) || t.DisplayName.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));

        foreach (var host in _state.Hosts)
        {
            Hosts.Add(new HostOption(host));
        }

        SelectedHost = Hosts.FirstOrDefault(h => h.Host.Id == _state.SelectedHostId) ?? Hosts.FirstOrDefault();
    }

    public ObservableCollection<HostOption> Hosts { get; } = [];
    public ObservableCollection<AppTileViewModel> Apps { get; } = [];
    public ICollectionView AppsView { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHost), nameof(HasNoHost))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(RemoveHostCommand), nameof(SaveCredentialsCommand), nameof(DeleteCredentialsCommand))]
    private HostOption? _selectedHost;

    [ObservableProperty]
    private AppTileViewModel? _selectedApp;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isWarning;

    public bool HasHost => SelectedHost is not null;
    public bool HasNoHost => SelectedHost is null;
    public bool HasNoApps => SelectedHost is { Host.Apps.Count: 0 } && !IsBusy; // the desktop tile is always there

    partial void OnSearchTextChanged(string value) => AppsView.Refresh();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(HasNoApps));

    partial void OnSelectedHostChanged(HostOption? value)
    {
        OnPropertyChanged(nameof(AskBeforeReleasingSession));
        ShowCachedApps();
        if (value is not null)
        {
            _state = _state with { SelectedHostId = value.Host.Id };
            _store.Save(_state);
            _ = RefreshAsync();
        }
    }

    [RelayCommand]
    private void AddHost()
    {
        var result = _dialogs.AddHost();
        if (result is not null)
        {
            SavePaired(result, existing: null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (SelectedHost is not { } option)
        {
            return;
        }

        IsBusy = true;
        SetStatus(Strings.T("client.status.syncing"), warning: false);
        try
        {
            var synced = await _sync.SyncAsync(option.Host);
            ReplaceHost(synced);
            SetStatus(Strings.T("client.status.synced", synced.Apps.Count, DateTime.Now.ToString("t", CultureInfo.CurrentCulture)), warning: false);
        }
        catch (AgentUnreachableException ex)
        {
            _logger.LogWarning(ex, "Host {Address} offline", option.Host.Address);
            var when = option.Host.LastSyncUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? Strings.T("client.status.never");
            SetStatus(Strings.T("client.status.offline", when), warning: true);
        }
        catch (InvalidTokenException ex)
        {
            SetStatus(ex.Message, warning: true);
            if (_dialogs.Confirm(Strings.T("client.dialogs.invalidTokenTitle"), Strings.T("client.dialogs.askNewToken", ex.Message)))
            {
                var result = _dialogs.AddHost(option.Host.Address, option.Host.AgentPort);
                if (result is not null)
                {
                    SavePaired(result, option.Host);
                }
            }
        }
        catch (CertificateChangedException ex)
        {
            SetStatus(Strings.T("client.status.hostChanged"), warning: true);
            var message = Strings.T("client.dialogs.hostChangedMessage", ex.Message,
                CertificateFingerprint.Format(option.Host.PinnedFingerprint), CertificateFingerprint.Format(ex.ObservedFingerprint));
            if (_dialogs.Confirm(Strings.T("client.dialogs.hostChangedTitle"), message))
            {
                ReplaceHost(option.Host with { PinnedFingerprint = ex.ObservedFingerprint });
                _logger.LogWarning("User accepted new fingerprint for {Address}", option.Host.Address);
                IsBusy = false;
                await RefreshAsync();
            }
        }
        catch (AgentException ex)
        {
            SetStatus(ex.Message, warning: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to store sync results for {Address}", option.Host.Address);
            SetStatus(Strings.T("client.status.saveFailed", ex.Message), warning: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => SelectedHost is not null && !IsBusy;

    [RelayCommand]
    private async Task OpenAppAsync(AppTileViewModel? tile)
    {
        if (tile is null || SelectedHost is not { } option || IsProgressVisible)
        {
            return;
        }

        if (!_state.SigningPromptDismissed && !_signer.GetStatus().Configured)
        {
            if (_dialogs.Confirm(Strings.T("client.signing.title"), Strings.T("client.signing.prompt")))
            {
                await ConfigureSigningCoreAsync();
            }
            else
            {
                _state = _state with { SigningPromptDismissed = true };
                _store.Save(_state);
                SetStatus(Strings.T("client.status.signingLater"), warning: false);
            }
        }

        if (!await EnsureHostFreeAsync(option, tile.IsDesktop))
        {
            ProgressMessage = null;
            ProgressDetail = null;
            return;
        }

        try
        {
            using var process = tile.IsDesktop ? _launcher.LaunchDesktop(option.Host) : _launcher.Launch(option.Host, tile.App);
            ProgressMessage = tile.IsDesktop ? Strings.T("client.progress.openingDesktop") : Strings.T("client.progress.openingApp", tile.DisplayName);
            ProgressDetail = Strings.T("client.progress.connecting");
            await RemoteAppLauncher.WaitForRdpWindowAsync(process, TimeSpan.FromSeconds(15));
            SetStatus(Strings.T("client.status.opened", tile.DisplayName), warning: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Failed to launch {Alias}", tile.App.Alias);
            _dialogs.Error(Strings.T("client.errors.openFailed", tile.DisplayName, ex.Message));
        }
        finally
        {
            ProgressMessage = null;
            ProgressDetail = null;
        }
    }

    /// <summary>Non-null while a blocking step runs (releasing the host, opening an app): shows the overlay.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressVisible))]
    private string? _progressMessage;

    [ObservableProperty]
    private string? _progressDetail;

    public bool IsProgressVisible => ProgressMessage is not null;

    /// <summary>
    /// A user cannot have a desktop session and a RemoteApp session on Windows client editions.
    /// If the host is in local use, ask (or, if configured, just proceed) and log that session off.
    /// Returns false when the app must not be launched.
    /// For the full desktop (<paramref name="desktop"/>) the user's own desktop is reused and their open
    /// RemoteApps are closed instead; agents older than 0.6.0 only know the RemoteApp rule (which would
    /// log off the very desktop being opened), so nothing is released and Windows handles it as usual.
    /// </summary>
    private async Task<bool> EnsureHostFreeAsync(HostOption option, bool desktop = false)
    {
        var host = option.Host;
        if (desktop && !host.Supports(AgentFeatures.DesktopSession))
        {
            _logger.LogInformation("Agent of {Address} has no desktop session rule; opening the desktop without releasing", host.Address);
            return true;
        }

        try
        {
            using var agent = _agents(host);
            var status = await agent.GetSessionStatusAsync(host.RdpUserName, desktop);
            if (status is not { Busy: true })
            {
                return true;
            }

            if (host.AskBeforeReleasingSession)
            {
                var decision = _dialogs.AskReleaseSession(host.DisplayName, status.Sessions);
                if (decision is null)
                {
                    SetStatus(Strings.T("client.status.openCancelled"), warning: false);
                    return false;
                }

                if (decision.Always)
                {
                    ReplaceHost(host with { AskBeforeReleasingSession = false });
                    OnPropertyChanged(nameof(AskBeforeReleasingSession));
                }
            }

            IsBusy = true;
            ProgressMessage = !status.Sessions.Any(s => s.Action == SessionActions.Logoff) ? Strings.T("client.progress.releasing")
                : desktop ? Strings.T("client.progress.closingApps")
                : Strings.T("client.progress.loggingOff");
            ProgressDetail = desktop
                ? Strings.T("client.progress.desktopNext")
                : Strings.T("client.progress.appNext");
            var result = await agent.ReleaseSessionAsync(host.RdpUserName, desktop);
            _logger.LogInformation("Released {Count} host sessions on {Address}", result.Released, host.Address);
            if (result.Busy)
            {
                _dialogs.Error(Strings.T("client.errors.releaseFailed", host.DisplayName));
                return false;
            }

            // Give Windows a moment to finish tearing down the session before the RDP connection.
            await Task.Delay(TimeSpan.FromSeconds(2));
            var who = string.Join(", ", status.Sessions.Select(s => s.User).Distinct());
            SetStatus(desktop ? Strings.T("client.status.releasedForDesktop", who) : Strings.T("client.status.releasedForApp", who), warning: false);
            return true;
        }
        catch (AgentException ex)
        {
            // Offline agent, old agent or auth problems: keep the previous behavior and just launch.
            _logger.LogWarning(ex, "Host session check skipped for {Address}", host.Address);
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Menu toggle: ask before logging off the host's session (default: don't ask).</summary>
    public bool AskBeforeReleasingSession
    {
        get => SelectedHost?.Host.AskBeforeReleasingSession ?? false;
        set
        {
            if (SelectedHost is { } option && option.Host.AskBeforeReleasingSession != value)
            {
                ReplaceHost(option.Host with { AskBeforeReleasingSession = value });
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    private async Task ConfigureSigningAsync()
    {
        var status = _signer.GetStatus();
        if (status.Configured)
        {
            _dialogs.Info(Strings.T("client.signing.alreadyConfigured", RdpSigning.Describe(status)));
            return;
        }

        if (_dialogs.Confirm(Strings.T("client.signing.title"), Strings.T("client.signing.prompt")))
        {
            await ConfigureSigningCoreAsync();
        }
    }

    private async Task ConfigureSigningCoreAsync()
    {
        switch (await _signingSetup.ConfigureAsync())
        {
            case SigningSetupResult.Configured:
                var resigned = _launcher.SignExistingFiles();
                _logger.LogInformation("RDP signing configured; {Count} existing files re-signed", resigned);
                SetStatus(Strings.T("client.status.signingConfigured"), warning: false);
                break;
            case SigningSetupResult.Cancelled:
                SetStatus(Strings.T("client.status.signingCancelled"), warning: true);
                break;
            default:
                _dialogs.Error(Strings.T("client.errors.signingFailed", ClientPaths.LogDirectory));
                break;
        }
    }

    [RelayCommand]
    private void RenameApp(AppTileViewModel? tile)
    {
        tile ??= SelectedApp;
        if (tile is not { CanRename: true })
        {
            return;
        }

        var name = _dialogs.AskAppName(tile.DisplayName, tile.App.DisplayName);
        if (name is not null)
        {
            SetAppName(tile, name);
        }
    }

    [RelayCommand]
    private void ResetAppName(AppTileViewModel? tile)
    {
        if (tile is not null)
        {
            SetAppName(tile, null);
        }
    }

    /// <summary>Local name only (null restores the host's); the host is never changed.</summary>
    private void SetAppName(AppTileViewModel tile, string? name)
    {
        if (SelectedHost is not { } option || !tile.CanRename)
        {
            return;
        }

        var renamed = tile.App.WithCustomName(name);
        if (renamed == tile.App)
        {
            return;
        }

        _logger.LogInformation("App {Alias} local name set to {Name}", renamed.Alias, renamed.CustomName ?? "(host name)");
        ReplaceHost(option.Host with
        {
            Apps = option.Host.Apps.Select(a => string.Equals(a.Alias, renamed.Alias, StringComparison.OrdinalIgnoreCase) ? renamed : a).ToList(),
        });
        SetStatus(renamed.CustomName is null
            ? Strings.T("client.status.nameRestored", renamed.Name)
            : Strings.T("client.status.renamed", renamed.DisplayName, renamed.Name), warning: false);
    }

    [RelayCommand]
    private void CreateShortcut(AppTileViewModel? tile) => CreateShortcutIn(tile, directory: null);

    [RelayCommand]
    private void AddToStartMenu(AppTileViewModel? tile) => CreateShortcutIn(tile, DesktopShortcuts.StartMenuDirectory);

    /// <summary>A .lnk to the app's .rdp file: on the desktop (null) or in <paramref name="directory"/>.</summary>
    private void CreateShortcutIn(AppTileViewModel? tile, string? directory)
    {
        if (tile is null || SelectedHost is not { } option)
        {
            return;
        }

        try
        {
            if (tile.IsDesktop)
            {
                var desktopLnk = DesktopShortcuts.Create(Strings.T("client.desktop.shortcutName", option.Host.DisplayName),
                    _launcher.WriteDesktopRdpFile(option.Host), RemoteAppLauncher.MstscPath, directory);
                SetStatus(Strings.T("client.status.shortcutCreated", Path.GetFileName(desktopLnk)), warning: false);
                return;
            }

            var rdp = _launcher.WriteRdpFile(option.Host, tile.App);
            var png = _icons.PngPath(option.Host.Id, tile.App.Alias);
            var ico = _icons.IcoPath(option.Host.Id, tile.App.Alias);
            if (File.Exists(png))
            {
                File.WriteAllBytes(ico, IcoWriter.FromPng(File.ReadAllBytes(png)));
            }

            var lnk = DesktopShortcuts.Create(tile.DisplayName, rdp, File.Exists(ico) ? ico : RemoteAppLauncher.MstscPath, directory);
            SetStatus(Strings.T("client.status.shortcutCreated", Path.GetFileName(lnk)), warning: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogError(ex, "Failed to create shortcut for {Alias}", tile.App.Alias);
            _dialogs.Error(Strings.T("client.errors.shortcutFailed", ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(HasHost))]
    private void SaveCredentials()
    {
        var host = SelectedHost!.Host;
        var input = _dialogs.AskCredentials(host.DisplayName, host.RdpUserName);
        if (input is null)
        {
            return;
        }

        try
        {
            _credentials.Save(host.Address, input.UserName, input.Password);
            ReplaceHost(host with { RdpUserName = input.UserName });
            SetStatus(Strings.T("client.status.credentialsSaved"), warning: false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasHost))]
    private void DeleteCredentials()
    {
        var host = SelectedHost!.Host;
        _credentials.Delete(host.Address);
        ReplaceHost(host with { RdpUserName = null });
        SetStatus(Strings.T("client.status.credentialsDeleted"), warning: false);
    }

    [RelayCommand(CanExecute = nameof(HasHost))]
    private void RemoveHost()
    {
        var option = SelectedHost!;
        if (!_dialogs.Confirm(Strings.T("client.dialogs.removeHostTitle"), Strings.T("client.dialogs.removeHostMessage", option.Label)))
        {
            return;
        }

        _icons.DeleteHostFiles(option.Host.Id);
        _credentials.Delete(option.Host.Address);
        _state = _state with { Hosts = _state.Hosts.Where(h => h.Id != option.Host.Id).ToList() };
        Hosts.Remove(option);
        SelectedHost = Hosts.FirstOrDefault();
        _state = _state with { SelectedHostId = SelectedHost?.Host.Id };
        _store.Save(_state);
        if (SelectedHost is null)
        {
            SetStatus(string.Empty, warning: false);
        }
    }

    private void SavePaired(AddHostResult result, PairedHost? existing)
    {
        // Same address as an existing host => update it (keeps Id, cache, credentials).
        existing ??= _state.Hosts.FirstOrDefault(h =>
            string.Equals(h.Address, result.Address, StringComparison.OrdinalIgnoreCase) && h.AgentPort == result.AgentPort);

        var host = (existing ?? new PairedHost
        {
            Id = Guid.NewGuid(),
            DisplayName = result.Pairing.Host.HostName,
            Address = result.Address,
            AgentPort = result.AgentPort,
            ProtectedToken = string.Empty,
            PinnedFingerprint = string.Empty,
        }) with
        {
            ProtectedToken = HostStore.ProtectToken(result.Token),
            PinnedFingerprint = result.Pairing.ObservedFingerprint,
            RdpPort = result.Pairing.Host.RdpPort,
        };

        _logger.LogInformation("Paired host {Address}:{Port}", host.Address, host.AgentPort);
        if (existing is null)
        {
            _state = _state with { Hosts = [.. _state.Hosts, host] };
            _store.Save(_state);
            var option = new HostOption(host);
            Hosts.Add(option);
            SelectedHost = option;
        }
        else
        {
            ReplaceHost(host);
            _ = RefreshAsync();
        }
    }

    private void ReplaceHost(PairedHost host)
    {
        _state = _state with { Hosts = _state.Hosts.Select(h => h.Id == host.Id ? host : h).ToList() };
        _store.Save(_state);

        var option = Hosts.FirstOrDefault(h => h.Host.Id == host.Id);
        if (option is not null)
        {
            option.Host = host;
        }

        if (ReferenceEquals(option, SelectedHost))
        {
            ShowCachedApps();
        }
    }

    private void ShowCachedApps()
    {
        var selectedAlias = SelectedApp?.App.Alias;
        Apps.Clear();
        if (SelectedHost is { } option)
        {
            Apps.Add(AppTileViewModel.Desktop()); // always first
            foreach (var app in option.Host.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Apps.Add(new AppTileViewModel(app, _icons.PngPath(option.Host.Id, app.Alias)));
            }
        }

        // Keep the selection across syncs and renames.
        SelectedApp = Apps.FirstOrDefault(a => string.Equals(a.App.Alias, selectedAlias, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(HasNoApps));
    }

    private void SetStatus(string text, bool warning)
    {
        Status = text;
        IsWarning = warning;
    }
}
