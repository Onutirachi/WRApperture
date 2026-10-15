using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Agent;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Publishing;
using WRApperture.Host.Core.StartWith;
using WRApperture.Host.Manager.Services;
using WRApperture.Shared.Localization;
using WRApperture.Shared.Updates;

namespace WRApperture.Host.Manager.ViewModels;

/// <param name="startWith">What starts with the app in remote sessions (shown under it).</param>
public sealed class AppItemViewModel(PublishedApp app, ImageSource? icon, IReadOnlyList<StartWithProgram> startWith)
{
    public PublishedApp App { get; } = app;
    public ImageSource? Icon { get; } = icon;
    public string DisplayName => App.DisplayName;
    public string Path => App.ExecutablePath;

    public string Details => App.CommandLinePolicy switch
    {
        CommandLinePolicy.Required => Strings.T("manager.app.arguments", App.RequiredArguments),
        CommandLinePolicy.Any => Strings.T("manager.app.anyArguments"),
        _ => string.Empty,
    } + (App.ShowInList ? string.Empty : "  ·  " + Strings.T("manager.app.hidden"))
      + (startWith.Count == 0 ? string.Empty : "  ·  " + Strings.T("manager.app.startsWith", string.Join(", ", startWith.Select(p => p.Name))));
}

/// <summary>A client paired by approval (it has its own token).</summary>
public sealed record DeviceItem(Guid Id, string Name, string Details);

public sealed partial class ManagerViewModel : ObservableObject
{
    private readonly IAppPublisher _publisher;
    private readonly IAppSourceInspector _inspector;
    private readonly IAppDiscovery _discovery;
    private readonly IIconProvider _icons;
    private readonly IHostPreparation _preparation;
    private readonly ILocalAccounts _accounts;
    private readonly IStartWithResolver _startWith;
    private readonly INetworkProfiles _networks;
    private readonly IAgentSettingsStore _agentSettings;
    private readonly IAgentService _agent;
    private readonly IManagerDialogs _dialogs;
    private readonly IUpdateChecker _updates;
    private readonly ILogger<ManagerViewModel> _logger;

    public ManagerViewModel(IAppPublisher publisher, IAppSourceInspector inspector, IAppDiscovery discovery, IIconProvider icons,
        IHostPreparation preparation, ILocalAccounts accounts, IStartWithResolver startWith, INetworkProfiles networks,
        IAgentSettingsStore agentSettings, IAgentService agent, IManagerDialogs dialogs,
        IUpdateChecker updates, ILogger<ManagerViewModel> logger, bool isAdmin)
    {
        _updates = updates;
        _publisher = publisher;
        _inspector = inspector;
        _discovery = discovery;
        _icons = icons;
        _preparation = preparation;
        _accounts = accounts;
        _startWith = startWith;
        _networks = networks;
        _agentSettings = agentSettings;
        _agent = agent;
        _dialogs = dialogs;
        _logger = logger;
        IsAdmin = isAdmin;
        Refresh();
    }

    public bool IsAdmin { get; }

    /// <summary>
    /// Whose startup programs to show: the user at the PC's screen or, when nobody is there (this app used over
    /// Remote Desktop), the user running this app.
    /// </summary>
    internal static string? UserSid(ILocalAccounts accounts) =>
        accounts.ConsoleUser()?.Sid ?? System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
    public bool IsReadOnly => !IsAdmin;

    public ObservableCollection<AppItemViewModel> Apps { get; } = [];
    public ObservableCollection<DeviceItem> Devices { get; } = [];
    public bool HasDevices => Devices.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(RemoveCommand))]
    private AppItemViewModel? _selectedApp;

    [ObservableProperty] private string _agentState = string.Empty;
    [ObservableProperty] private bool _agentInstalled;
    [ObservableProperty] private string _port = string.Empty;
    [ObservableProperty] private string _paired = string.Empty;
    [ObservableProperty] private string _fingerprint = string.Empty;
    [ObservableProperty] private string _addresses = string.Empty;
    [ObservableProperty] private string _vpns = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isWarning;

    public bool HasNoApps => Apps.Count == 0;

    [RelayCommand]
    private void Refresh()
    {
        Apps.Clear();
        var sid = UserSid(_accounts);
        foreach (var app in _publisher.List())
        {
            Apps.Add(new AppItemViewModel(app, Icons.Load(_icons, app.IconPath, app.IconIndex), _startWith.For(app, sid)));
        }

        OnPropertyChanged(nameof(HasNoApps));

        RefreshReadiness();

        var status = _agent.GetStatus();
        AgentState = status.Text;
        AgentInstalled = status.Installed;
        var settings = _agentSettings.Load();
        Port = settings.Port.ToString();
        RefreshDevices();
        Fingerprint = _agent.Fingerprint(settings.CertificateThumbprint) ?? Strings.T("manager.connection.fingerprintPending");
        var ips = _agent.Ipv4Addresses();
        Addresses = ips.Count == 0 ? Strings.T("manager.connection.noNetwork") : string.Join(", ", ips);
        Vpns = ActiveVpns() is { Count: > 0 } vpns ? string.Join(", ", vpns) : Strings.T("manager.connection.noVpn");
        OnPropertyChanged(nameof(AutoUpdate));
    }

    /// <summary>Dropped files or files picked with "Add program or shortcut…": each one is reviewed in the editor.</summary>
    [RelayCommand(CanExecute = nameof(IsAdmin))]
    private void AddFiles(string[]? paths)
    {
        paths ??= _dialogs.PickPrograms();
        if (paths is null)
        {
            return;
        }

        var published = 0;
        foreach (var path in paths)
        {
            AppDraft draft;
            try
            {
                draft = _inspector.Inspect(path);
            }
            catch (AppValidationException ex)
            {
                _dialogs.Error(Strings.T("manager.errors.cannotUse", ex.Reason));
                continue;
            }

            var input = new EditorInput(Strings.T("manager.editor.publishTitle"), null, draft.DisplayName, draft.ExecutablePath,
                draft.Arguments is null ? CommandLinePolicy.None : CommandLinePolicy.Required, draft.Arguments,
                draft.IconPath, draft.IconIndex);
            if (_dialogs.EditApp(input, TrySave))
            {
                published++;
            }
        }

        if (published > 0)
        {
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    private void Edit()
    {
        var app = SelectedApp!.App;
        var input = new EditorInput(Strings.T("manager.editor.editTitle"), app.Alias, app.DisplayName, app.RawPath ?? app.ExecutablePath,
            app.CommandLinePolicy, app.RequiredArguments, app.IconPath, app.IconIndex, app.StartWithPrograms, app.AutoStartWith);
        if (_dialogs.EditApp(input, TrySave))
        {
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    private void Remove()
    {
        var app = SelectedApp!.App;
        if (!_dialogs.Confirm(Strings.T("manager.remove.title"), Strings.T("manager.remove.confirm", app.DisplayName)))
        {
            return;
        }

        Run(() => _publisher.Unpublish(app.Alias), Strings.T("manager.status.removed", app.DisplayName));
    }

    private bool CanModifySelected() => IsAdmin && SelectedApp is not null;

    [RelayCommand(CanExecute = nameof(IsAdmin))]
    private void PublishInstalled()
    {
        var chosen = _dialogs.ChooseInstalled(_discovery.Discover());
        if (chosen is null || chosen.Count == 0)
        {
            return;
        }

        var errors = new List<string>();
        foreach (var app in chosen)
        {
            try
            {
                _publisher.Publish(new PublishRequest(app.ExecutablePath, app.DisplayName, IconPath: app.IconPath, IconIndex: app.IconIndex,
                    CommandLinePolicy: app.Arguments is null ? CommandLinePolicy.None : CommandLinePolicy.Required,
                    RequiredArguments: app.Arguments));
            }
            catch (AppValidationException ex)
            {
                errors.Add($"{app.DisplayName}: {ex.Reason}");
            }
        }

        Refresh();
        SetStatus(Strings.T("manager.status.publishedCount", chosen.Count - errors.Count), warning: errors.Count > 0);
        if (errors.Count > 0)
        {
            _dialogs.Error(Strings.T("manager.errors.someNotPublished", string.Join("\n", errors)));
        }
    }

    [RelayCommand(CanExecute = nameof(IsAdmin))]
    private void NewToken()
    {
        if (_agentSettings.Load().IsPaired &&
            !_dialogs.Confirm(Strings.T("manager.newToken.title"), Strings.T("manager.newToken.confirm")))
        {
            return;
        }

        string? token = null;
        Run(() => token = _agentSettings.RegenerateToken(), Strings.T("manager.status.tokenGenerated"));
        if (token is not null)
        {
            _dialogs.ShowToken(new TokenInfo(token, Port, Addresses, Fingerprint));
        }
    }

    private IReadOnlyList<string> ActiveVpns()
    {
        try
        {
            return _networks.Connected().Where(n => n.IsVpn).Select(n => n.VpnName!).Distinct().ToList();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            _logger.LogWarning(ex, "Could not list the networks");
            return [];
        }
    }

    /// <summary>Re-reads the paired devices (cheap: a few registry values); the list only changes when they did.</summary>
    public void RefreshDevices()
    {
        var clients = _agentSettings.ListClients();
        if (clients.Select(c => c.Id).SequenceEqual(Devices.Select(d => d.Id)) && Paired.Length > 0)
        {
            return;
        }

        Devices.Clear();
        foreach (var client in clients)
        {
            var date = client.CreatedUtc.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture);
            Devices.Add(new DeviceItem(client.Id, client.Name, string.IsNullOrEmpty(client.User)
                ? Strings.T("manager.devices.details", date)
                : Strings.T("manager.devices.detailsUser", client.User, date)));
        }

        OnPropertyChanged(nameof(HasDevices));
        Paired = clients.Count > 0 ? Strings.T("manager.connection.pairedDevices", clients.Count)
            : _agentSettings.Load().IsPaired ? Strings.T("manager.connection.pairedYes")
            : Strings.T("manager.connection.pairedNo");
    }

    [RelayCommand(CanExecute = nameof(IsAdmin))]
    private void RemoveDevice(DeviceItem? device)
    {
        if (device is null ||
            !_dialogs.Confirm(Strings.T("manager.devices.removeTitle"), Strings.T("manager.devices.removeConfirm", device.Name)))
        {
            return;
        }

        Run(() => _agentSettings.RemoveClient(device.Id), Strings.T("manager.status.deviceRemoved", device.Name));
    }

    [RelayCommand(CanExecute = nameof(IsAdmin))]
    private async Task RestartAgentAsync()
    {
        SetStatus(Strings.T("manager.status.restartingAgent"), warning: false);
        try
        {
            await Task.Run(_agent.Restart);
            SetStatus(Strings.T("manager.status.agentRestarted"), warning: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            _logger.LogError(ex, "Agent restart failed");
            _dialogs.Error(Strings.T("manager.errors.restartAgent", ex.Message));
        }

        Refresh();
    }

    [RelayCommand]
    private void Elevate()
    {
        if (Elevation.TryRelaunchElevated())
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    private string? TrySave(PublishRequest request)
    {
        try
        {
            var result = _publisher.Publish(request);
            var warning = result.Warnings.Contains(PublishWarnings.WritableLocation);
            SetStatus(result.Outcome switch
            {
                PublishOutcome.Created => Strings.T("manager.status.published", result.App.DisplayName),
                PublishOutcome.Updated => Strings.T("manager.status.updated", result.App.DisplayName),
                _ => Strings.T("manager.status.unchanged", result.App.DisplayName),
            } + (warning ? " " + Strings.T("manager.status.writableLocation") : string.Empty), warning);
            return null;
        }
        catch (AppValidationException ex)
        {
            return ex.Reason;
        }
        catch (AdminRequiredException ex)
        {
            return ex.Message;
        }
    }

    private void Run(Action action, string success)
    {
        try
        {
            action();
            SetStatus(success, warning: false);
        }
        catch (Exception ex) when (ex is AdminRequiredException or AppValidationException or HostNotSupportedException
                                       or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Operation failed");
            _dialogs.Error(ex.Message);
        }

        Refresh();
    }

    private void SetStatus(string text, bool warning)
    {
        Status = text;
        IsWarning = warning;
    }
}
