using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Manager.ViewModels;

/// <param name="Description">The requirement when it is met ("The account has a password"), the problem otherwise.</param>
/// <param name="Detail">What is wrong and why it matters (empty when OK).</param>
/// <param name="ActionLabel">Text of the item's button; null when there is nothing to do.</param>
public sealed record ReadinessItem(ReadinessCheck Check, string Description, string Detail, string? ActionLabel)
{
    public ReadinessState State => Check.State;
    public bool HasDetail => Detail.Length > 0;
    public bool HasAction => ActionLabel is not null;
}

/// <summary>The "This PC" panel: what remote access still needs, each with its own Resolve button.</summary>
public sealed partial class ManagerViewModel
{
    /// <summary>Shown whatever their state: the ones a user can actually get wrong.</summary>
    private static readonly HashSet<ReadinessCheckId> AlwaysShown =
        [ReadinessCheckId.RdpEnabled, ReadinessCheckId.FirewallRdp, ReadinessCheckId.NetworkProfile, ReadinessCheckId.AccountPassword];

    /// <summary>Never shown: security defaults that "Resolve all" still enforces quietly.</summary>
    private static readonly HashSet<ReadinessCheckId> NeverShown =
        [ReadinessCheckId.Nla, ReadinessCheckId.AllowListEnforced, ReadinessCheckId.LanguageBarHidden];

    /// <summary>Fixed by <see cref="Core.IHostPreparation.Prepare"/>; the others need their own <c>Fix</c>.</summary>
    private static readonly HashSet<ReadinessCheckId> PreparedTogether =
    [
        ReadinessCheckId.RdpEnabled, ReadinessCheckId.RdpPolicy, ReadinessCheckId.RdpService, ReadinessCheckId.Nla,
        ReadinessCheckId.AllowListEnforced, ReadinessCheckId.FirewallRdp, ReadinessCheckId.LanguageBarHidden,
    ];

    private IReadOnlyList<ReadinessCheck> _checks = [];

    public ObservableCollection<ReadinessItem> Readiness { get; } = [];

    /// <summary>"Resolve all" is offered: an administrator can fix something automatically.</summary>
    [ObservableProperty] private bool _hasPendingReadiness;

    /// <summary>Something keeps remote access from working (fixable here or not): the page is flagged in the sidebar.</summary>
    [ObservableProperty] private bool _needsAttention;

    private void RefreshReadiness(IReadOnlyList<ReadinessCheck>? checks = null)
    {
        _checks = checks ?? _preparation.Check();
        Readiness.Clear();
        foreach (var check in _checks.Where(IsShown))
        {
            Readiness.Add(new ReadinessItem(check, TitleOf(check), DetailOf(check), ActionOf(check)));
        }

        HasPendingReadiness = IsAdmin && PendingAutomatic().Any();
        NeedsAttention = Readiness.Any(r => r.State is ReadinessState.Pending or ReadinessState.NotSupported);
        FixItemCommand.NotifyCanExecuteChanged();
    }

    private static bool IsShown(ReadinessCheck check) =>
        check.Id == ReadinessCheckId.Edition ? check.State == ReadinessState.NotSupported
        : !NeverShown.Contains(check.Id) && (AlwaysShown.Contains(check.Id) || check.State != ReadinessState.Ok);

    private IEnumerable<ReadinessCheck> PendingAutomatic() =>
        _checks.Where(c => c.State == ReadinessState.Pending && c.Fix == ReadinessFix.Automatic);

    private static string? ActionOf(ReadinessCheck check) => check.State switch
    {
        ReadinessState.Ok or ReadinessState.NotSupported => null,
        _ => check.Fix switch
        {
            ReadinessFix.Automatic => Strings.T("manager.readiness.resolve"),
            ReadinessFix.SetPassword => Strings.T("manager.readiness.setPassword"),
            ReadinessFix.OpenSettings when check.SettingsUri is not null => Strings.T("manager.readiness.openSettings"),
            _ => null,
        },
    };

    /// <summary>
    /// Checks are worded as requirements ("Remote Desktop enabled"); next to a warning that reads as if it were true,
    /// so a check that is not met says what is wrong instead.
    /// </summary>
    private string TitleOf(ReadinessCheck check) => check.State == ReadinessState.Ok ? check.Description : check.Id switch
    {
        ReadinessCheckId.RdpEnabled => Strings.T("manager.problem.rdpEnabled"),
        ReadinessCheckId.RdpPolicy => Strings.T("manager.problem.rdpPolicy"),
        ReadinessCheckId.RdpService => Strings.T("manager.problem.rdpService"),
        ReadinessCheckId.FirewallRdp => Strings.T("manager.problem.firewall"),
        ReadinessCheckId.NetworkProfile => Strings.T("manager.problem.network"),
        ReadinessCheckId.AccountPassword => Strings.T("manager.problem.accountPassword", ConsoleUserName()),
        ReadinessCheckId.RemoteDesktopUsers => Strings.T("manager.problem.remoteUsers", ConsoleUserName()),
        ReadinessCheckId.MicrosoftAccount when check.State == ReadinessState.Pending => Strings.T("manager.problem.passwordless"),
        ReadinessCheckId.Sleep => Strings.T("manager.problem.sleep"),
        _ => check.Description,
    };

    private static string DetailOf(ReadinessCheck check)
    {
        if (check.State == ReadinessState.Ok)
        {
            return string.Empty;
        }

        return check.Id switch
        {
            ReadinessCheckId.Edition => Strings.T("manager.readiness.notSupported", check.CurrentValue),
            ReadinessCheckId.RdpEnabled => Strings.T("manager.readiness.rdpEnabledHint"),
            ReadinessCheckId.RdpPolicy => Strings.T("manager.readiness.rdpPolicyHint"),
            ReadinessCheckId.RdpService => Strings.T("manager.readiness.rdpServiceHint", check.CurrentValue),
            ReadinessCheckId.FirewallRdp => Strings.T("manager.readiness.firewallHint", check.CurrentValue),
            ReadinessCheckId.NetworkProfile => Strings.T("manager.readiness.networkHint", check.CurrentValue),
            ReadinessCheckId.AccountPassword => Strings.T("manager.readiness.passwordHint"),
            ReadinessCheckId.RemoteDesktopUsers => Strings.T("manager.readiness.remoteUsersHint"),
            ReadinessCheckId.MicrosoftAccount when check.State == ReadinessState.Pending => Strings.T("manager.readiness.passwordlessHint"),
            ReadinessCheckId.MicrosoftAccount => Strings.T("manager.readiness.microsoftAccountHint", check.CurrentValue),
            ReadinessCheckId.Sleep => Strings.T("manager.readiness.sleepHint", check.CurrentValue),
            _ => Strings.T("manager.readiness.pending", check.CurrentValue),
        };
    }

    /// <summary>What a fix changes, why, and its side effect: shown before anything is changed.</summary>
    private string Explain(ReadinessCheck check)
    {
        var (what, why, effect) = check.Id switch
        {
            ReadinessCheckId.RdpEnabled => (Strings.T("manager.fix.rdpEnabledWhat"), Strings.T("manager.fix.rdpEnabledWhy"), Strings.T("manager.fix.rdpEnabledEffect")),
            ReadinessCheckId.RdpPolicy => (Strings.T("manager.fix.rdpPolicyWhat"), Strings.T("manager.fix.rdpPolicyWhy"), Strings.T("manager.fix.rdpPolicyEffect")),
            ReadinessCheckId.RdpService => (Strings.T("manager.fix.rdpServiceWhat"), Strings.T("manager.fix.rdpServiceWhy"), Strings.T("manager.fix.noEffect")),
            ReadinessCheckId.FirewallRdp => (Strings.T("manager.fix.firewallWhat"), Strings.T("manager.fix.firewallWhy"), Strings.T("manager.fix.firewallEffect")),
            ReadinessCheckId.NetworkProfile => (Strings.T("manager.fix.networkWhat", NetworkNames(check)), Strings.T("manager.fix.networkWhy"), Strings.T("manager.fix.networkEffect")),
            ReadinessCheckId.RemoteDesktopUsers => (Strings.T("manager.fix.remoteUsersWhat", ConsoleUserName()), Strings.T("manager.fix.remoteUsersWhy"), Strings.T("manager.fix.remoteUsersEffect")),
            ReadinessCheckId.MicrosoftAccount => (Strings.T("manager.fix.passwordlessWhat"), Strings.T("manager.fix.passwordlessWhy"), Strings.T("manager.fix.passwordlessEffect")),
            _ => (check.Description, check.ExpectedValue, Strings.T("manager.fix.noEffect")),
        };
        return Strings.T("manager.fix.template", what, why, effect);
    }

    private string ConsoleUserName() => _accounts.ConsoleUser()?.UserName ?? Environment.UserName;

    /// <summary>"Public: Home, Office" → "Home, Office".</summary>
    private static string NetworkNames(ReadinessCheck check) =>
        check.CurrentValue[(check.CurrentValue.IndexOf(':') + 1)..].Trim();

    [RelayCommand(CanExecute = nameof(CanFixItem))]
    private void FixItem(ReadinessItem? item)
    {
        if (item is null)
        {
            return;
        }

        var check = item.Check;
        switch (check.Fix)
        {
            case ReadinessFix.OpenSettings:
                _dialogs.OpenSettings(check.SettingsUri!);
                return;
            case ReadinessFix.SetPassword:
                SetPassword(check);
                return;
            case ReadinessFix.Automatic when _dialogs.Confirm(Strings.T("manager.fix.title", check.Description), Explain(check)):
                RunFix(() => _preparation.Fix(check.Id), Strings.T("manager.status.fixed", check.Description));
                return;
        }
    }

    private bool CanFixItem(ReadinessItem? item) =>
        item is { HasAction: true } && (IsAdmin || item.Check.Fix == ReadinessFix.OpenSettings);

    private void SetPassword(ReadinessCheck check)
    {
        var password = _dialogs.AskNewPassword(ConsoleUserName());
        if (password is not null)
        {
            RunFix(() => _preparation.SetAccountPassword(password), Strings.T("manager.status.passwordSet"));
        }
    }

    /// <summary>One confirmation listing every change, then the preparation and the items it leaves out.</summary>
    [RelayCommand(CanExecute = nameof(IsAdmin))]
    private void Prepare()
    {
        var pending = PendingAutomatic().ToList();
        var shown = pending.Where(IsShown).ToList();
        if (shown.Count > 0 &&
            !_dialogs.Confirm(Strings.T("manager.fix.allTitle"), string.Join("\n\n", shown.Select(c => $"• {c.Description}\n{Explain(c)}"))))
        {
            return;
        }

        RunFix(() =>
        {
            var checks = _preparation.Prepare();
            foreach (var check in pending.Where(c => !PreparedTogether.Contains(c.Id)))
            {
                checks = _preparation.Fix(check.Id);
            }

            return checks;
        }, Strings.T("manager.status.prepared"));
    }

    private void RunFix(Func<IReadOnlyList<ReadinessCheck>> fix, string success)
    {
        try
        {
            RefreshReadiness(fix());
            var stillPending = _checks.Count(c => c.State == ReadinessState.Pending && IsShown(c));
            SetStatus(stillPending == 0 ? success : Strings.T("manager.status.stillPending", stillPending), warning: stillPending > 0);
        }
        catch (Exception ex) when (ex is AdminRequiredException or AppValidationException or HostNotSupportedException
                                       or InvalidOperationException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            _logger.LogError(ex, "Readiness fix failed");
            _dialogs.Error(ex is AppValidationException validation ? validation.Reason : ex.Message);
            RefreshReadiness();
        }
    }
}
