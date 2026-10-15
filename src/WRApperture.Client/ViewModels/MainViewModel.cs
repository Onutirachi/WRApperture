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
using WRApperture.Client.Core.Startup;
using WRApperture.Client.Core.Storage;
using WRApperture.Client.Core.Updates;
using WRApperture.Client.Services;
using WRApperture.Shared;
using WRApperture.Shared.Localization;
using WRApperture.Shared.Updates;

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
    private readonly StartupRegistration _startup;
    private readonly Func<PairedHost, IAgentApi> _agents;
    private readonly ClientUpdates _updates;
    private readonly ILogger<MainViewModel> _logger;
    private ClientState _state;

    public MainViewModel(HostStore store, IconStore icons, RemoteAppLauncher launcher, HostSync sync,
        ICredentialStore credentials, IDialogService dialogs, IRdpSigner signer, ISigningSetup signingSetup, StartupRegistration startup,
        Func<PairedHost, IAgentApi> agents, ClientUpdates updates,
        ILogger<MainViewModel> logger)
    {
        _updates = updates;
        _store = store;
        _icons = icons;
        _launcher = launcher;
        _sync = sync;
        _credentials = credentials;
        _dialogs = dialogs;
        _signer = signer;
        _signingSetup = signingSetup;
        _startup = startup;
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
        OnPropertyChanged(nameof(HostVersionText));
        ShowCachedApps();
        EvaluateHostUpdate();
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
        if (result is null)
        {
            return;
        }

        var host = SavePaired(result, existing: null);
        if (!_credentials.Exists(host.Address))
        {
            // Sign-in details now, rather than when the first app opens.
            AskAndSaveCredentials(host, firstTime: true);
        }
    }

    /// <summary>The list refreshes by itself this often (and when the window is activated after <see cref="StaleAfter"/>).</summary>
    public static readonly TimeSpan AutoSyncInterval = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    /// <summary>"Sync now" and F5: like the automatic sync, but asks what to do about a revoked token or a changed certificate.</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => SyncAsync(interactive: true);

    /// <summary>Background syncs (runs until the app exits). They never open dialogs: problems only show in the status bar.</summary>
    public async void StartAutoSync()
    {
        while (true)
        {
            await Task.Delay(AutoSyncInterval);
            if (CanRefresh())
            {
                await SyncAsync(interactive: false);
            }
        }
    }

    /// <summary>Coming back to the window shows fresh data.</summary>
    public void OnWindowActivated()
    {
        if (SelectedHost is { Host.LastSyncUtc: var last } && CanRefresh() &&
            (last is null || DateTimeOffset.UtcNow - last.Value > StaleAfter))
        {
            _ = SyncAsync(interactive: false);
        }
    }

    /// <summary>"PC-SALA · version 0.17.0" for the host menu.</summary>
    public string HostVersionText => SelectedHost is not { } option ? string.Empty
        : option.Host.AgentVersion is { } version ? Strings.T("client.main.hostVersion", option.Label, version)
        : Strings.T("client.main.hostVersionUnknown", option.Label);

    /// <summary>"WRApperture 0.17.0" for the settings menu.</summary>
    public string ClientVersionText => Strings.T("client.main.clientVersion", ProductVersion.Format(_updates.Current));

    private async Task SyncAsync(bool interactive)
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
            ShowHostUpdateWarning();
            ShowHostIssues(synced);
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
            if (interactive && _dialogs.Confirm(Strings.T("client.dialogs.invalidTokenTitle"), Strings.T("client.dialogs.askNewToken", ex.Message)))
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
            if (interactive && _dialogs.Confirm(Strings.T("client.dialogs.hostChangedTitle"), message))
            {
                ReplaceHost(option.Host with { PinnedFingerprint = ex.ObservedFingerprint });
                _logger.LogWarning("User accepted new fingerprint for {Address}", option.Host.Address);
                IsBusy = false;
                await SyncAsync(interactive);
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

    /// <summary>What the host reported as keeping remote access from working, with where to fix it.</summary>
    private void ShowHostIssues(PairedHost host)
    {
        var issues = host.Issues.Select(IssueText).OfType<string>().ToList();
        if (issues.Count > 0)
        {
            SetStatus(Strings.T("client.status.hostNotReady", host.DisplayName, string.Join("; ", issues)), warning: true);
        }
    }

    private static string? IssueText(string code) => code switch
    {
        HostIssues.RdpDisabled => Strings.T("client.issues.rdpDisabled"),
        HostIssues.FirewallBlocked => Strings.T("client.issues.firewallBlocked"),
        HostIssues.PublicNetwork => Strings.T("client.issues.publicNetwork"),
        HostIssues.AccountWithoutPassword => Strings.T("client.issues.accountWithoutPassword"),
        HostIssues.AccountNotAllowed => Strings.T("client.issues.accountNotAllowed"),
        HostIssues.WindowsHelloOnly => Strings.T("client.issues.windowsHelloOnly"),
        _ => null, // codes from newer agents
    };

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

    /// <summary>Menu toggle: closing the window keeps the client in the tray (default on).</summary>
    public bool CloseToTray
    {
        get => _state.CloseToTray;
        set
        {
            if (_state.CloseToTray != value)
            {
                _state = _state with { CloseToTray = value };
                _store.Save(_state);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Menu toggle: start hidden in the tray when signing in to Windows (HKCU Run entry).</summary>
    public bool StartWithWindows
    {
        get => _startup.IsEnabled;
        set
        {
            try
            {
                _startup.Set(value, Environment.ProcessPath!);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                _logger.LogError(ex, "Could not change the Windows startup entry");
                _dialogs.Error(Strings.T("client.errors.startupFailed", ex.Message));
            }

            OnPropertyChanged();
        }
    }

    /// <summary>True only the first time the window is hidden to the tray, so the notice shows once.</summary>
    public bool ConsumeTrayHint()
    {
        if (_state.TrayHintShown)
        {
            return false;
        }

        _state = _state with { TrayHintShown = true };
        _store.Save(_state);
        return true;
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
                var desktopIco = _icons.WriteIco(option.Host.Id, tile.App.Alias, ShortcutIcons.ForDesktop());
                var desktopLnk = DesktopShortcuts.Create(Strings.T("client.shortcut.name", tile.DisplayName, option.Host.DisplayName),
                    _launcher.WriteDesktopRdpFile(option.Host), desktopIco, directory);
                SetStatus(Strings.T("client.status.shortcutCreated", Path.GetFileName(desktopLnk)), warning: false);
                return;
            }

            var rdp = _launcher.WriteRdpFile(option.Host, tile.App);
            var png = _icons.PngPath(option.Host.Id, tile.App.Alias);
            // The app's icon plus the WRApperture badge, to tell it from a local copy of the app.
            var ico = File.Exists(png)
                ? _icons.WriteIco(option.Host.Id, tile.App.Alias, ShortcutIcons.ForApp(File.ReadAllBytes(png)))
                : RemoteAppLauncher.MstscPath;

            // "<app> - <host>", so the same app published by two hosts gets two distinct shortcuts.
            var lnk = DesktopShortcuts.Create(Strings.T("client.shortcut.name", tile.DisplayName, option.Host.DisplayName), rdp, ico, directory);
            SetStatus(Strings.T("client.status.shortcutCreated", Path.GetFileName(lnk)), warning: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                       or FormatException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogError(ex, "Failed to create shortcut for {Alias}", tile.App.Alias);
            _dialogs.Error(Strings.T("client.errors.shortcutFailed", ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(HasHost))]
    private void SaveCredentials() => AskAndSaveCredentials(SelectedHost!.Host, firstTime: false);

    /// <summary>Asks for the host's Windows sign-in, pre-filled with what the host suggests, and saves it for mstsc.</summary>
    private void AskAndSaveCredentials(PairedHost host, bool firstTime)
    {
        var input = _dialogs.AskCredentials(host.DisplayName, host.RdpUserName ?? host.SuggestedUser,
            new CredentialsContext(host.MicrosoftAccount, firstTime));
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

    private PairedHost SavePaired(AddHostResult result, PairedHost? existing)
    {
        // Same address, or the same agent found under another address => update it (keeps Id and cache).
        existing ??= _state.Hosts.FirstOrDefault(h =>
            string.Equals(h.Address, result.Address, StringComparison.OrdinalIgnoreCase) && h.AgentPort == result.AgentPort)
            ?? _state.Hosts.FirstOrDefault(h =>
                string.Equals(h.PinnedFingerprint, result.Pairing.ObservedFingerprint, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !string.Equals(existing.Address, result.Address, StringComparison.OrdinalIgnoreCase))
        {
            // Saved credentials belong to the old address (and cannot be read back to move them).
            _credentials.Delete(existing.Address);
            existing = existing with { RdpUserName = null };
        }

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
            Address = result.Address,
            AgentPort = result.AgentPort,
            ProtectedToken = HostStore.ProtectToken(result.Token),
            PinnedFingerprint = result.Pairing.ObservedFingerprint,
            RdpPort = result.Pairing.Host.RdpPort,
            Issues = result.Pairing.Host.Issues ?? [],
            SuggestedUser = result.Pairing.Host.SuggestedUser,
            MicrosoftAccount = result.Pairing.Host.MicrosoftAccount,
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

        return host;
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
            OnPropertyChanged(nameof(HostVersionText));
        }
    }

    /// <summary>A host's tiles as the grid shows them: the desktop first, then the apps by name.</summary>
    public IReadOnlyList<AppTileViewModel> TilesFor(HostOption option) =>
        new[] { AppTileViewModel.Desktop() }
            .Concat(option.Host.Apps
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(app => new AppTileViewModel(app, _icons.PngPath(option.Host.Id, app.Alias))))
            .ToList();

    /// <summary>Tray menu: opens an app of any host, switching to that host first if needed.</summary>
    public void OpenFromTray(HostOption option, AppTileViewModel tile)
    {
        if (!ReferenceEquals(SelectedHost, option))
        {
            SelectedHost = option; // rebuilds Apps from the cache right away (the sync runs in the background)
        }

        var match = Apps.FirstOrDefault(a => a.IsDesktop == tile.IsDesktop &&
                                             string.Equals(a.App.Alias, tile.App.Alias, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            OpenAppCommand.Execute(match);
        }
    }

    private void ShowCachedApps()
    {
        var selectedAlias = SelectedApp?.App.Alias;
        Apps.Clear();
        if (SelectedHost is { } option)
        {
            foreach (var tile in TilesFor(option))
            {
                Apps.Add(tile);
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
