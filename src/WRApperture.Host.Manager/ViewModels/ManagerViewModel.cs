using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Agent;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Publishing;
using WRApperture.Host.Manager.Services;
using WRApperture.Shared.Localization;
using WRApperture.Shared.Updates;

namespace WRApperture.Host.Manager.ViewModels;

public sealed class AppItemViewModel(PublishedApp app, ImageSource? icon)
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
    } + (App.ShowInList ? string.Empty : "  ·  " + Strings.T("manager.app.hidden"));
}

public sealed record ReadinessItem(string Description, bool IsOk, string Detail);

public sealed partial class ManagerViewModel : ObservableObject
{
    private readonly IAppPublisher _publisher;
    private readonly IAppSourceInspector _inspector;
    private readonly IAppDiscovery _discovery;
    private readonly IIconProvider _icons;
    private readonly IHostPreparation _preparation;
    private readonly IAgentSettingsStore _agentSettings;
    private readonly IAgentService _agent;
    private readonly IManagerDialogs _dialogs;
    private readonly IUpdateChecker _updates;
    private readonly ILogger<ManagerViewModel> _logger;

    public ManagerViewModel(IAppPublisher publisher, IAppSourceInspector inspector, IAppDiscovery discovery, IIconProvider icons,
        IHostPreparation preparation, IAgentSettingsStore agentSettings, IAgentService agent, IManagerDialogs dialogs,
        IUpdateChecker updates, ILogger<ManagerViewModel> logger, bool isAdmin)
    {
        _updates = updates;
        _publisher = publisher;
        _inspector = inspector;
        _discovery = discovery;
        _icons = icons;
        _preparation = preparation;
        _agentSettings = agentSettings;
        _agent = agent;
        _dialogs = dialogs;
        _logger = logger;
        IsAdmin = isAdmin;
        Refresh();
    }

    public bool IsAdmin { get; }
    public bool IsReadOnly => !IsAdmin;

    public ObservableCollection<AppItemViewModel> Apps { get; } = [];
    public ObservableCollection<ReadinessItem> Readiness { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(RemoveCommand))]
    private AppItemViewModel? _selectedApp;

    [ObservableProperty] private bool _hasPendingReadiness;
    [ObservableProperty] private string _agentState = string.Empty;
    [ObservableProperty] private bool _agentInstalled;
    [ObservableProperty] private string _port = string.Empty;
    [ObservableProperty] private string _paired = string.Empty;
    [ObservableProperty] private string _fingerprint = string.Empty;
    [ObservableProperty] private string _addresses = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isWarning;

    public bool HasNoApps => Apps.Count == 0;

    [RelayCommand]
    private void Refresh()
    {
        Apps.Clear();
        foreach (var app in _publisher.List())
        {
            Apps.Add(new AppItemViewModel(app, Icons.Load(_icons, app.IconPath, app.IconIndex)));
        }

        OnPropertyChanged(nameof(HasNoApps));

        Readiness.Clear();
        foreach (var check in _preparation.Check())
        {
            Readiness.Add(new ReadinessItem(check.Description, check.State == ReadinessState.Ok,
                check.State switch
                {
                    // Raw values ("0", "1") mean nothing to users; only the edition is worth showing when OK.
                    ReadinessState.Ok => check.Id == ReadinessCheckId.Edition ? check.CurrentValue : string.Empty,
                    ReadinessState.NotSupported => Strings.T("manager.readiness.notSupported", check.CurrentValue),
                    _ => Strings.T("manager.readiness.pending", check.CurrentValue),
                }));
        }

        HasPendingReadiness = Readiness.Any(r => !r.IsOk);

        var status = _agent.GetStatus();
        AgentState = status.Text;
        AgentInstalled = status.Installed;
        var settings = _agentSettings.Load();
        Port = settings.Port.ToString();
        Paired = settings.IsPaired ? Strings.T("manager.connection.pairedYes") : Strings.T("manager.connection.pairedNo");
        Fingerprint = _agent.Fingerprint(settings.CertificateThumbprint) ?? Strings.T("manager.connection.fingerprintPending");
        var ips = _agent.Ipv4Addresses();
        Addresses = ips.Count == 0 ? Strings.T("manager.connection.noNetwork") : string.Join(", ", ips);
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
                draft.IconPath, draft.IconIndex, ShowInList: true);
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
            app.CommandLinePolicy, app.RequiredArguments, app.IconPath, app.IconIndex, app.ShowInList);
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
    private void Prepare() => Run(() => _preparation.Prepare(), Strings.T("manager.status.prepared"));

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
