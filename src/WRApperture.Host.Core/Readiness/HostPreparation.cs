using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Publishing;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Core.Readiness;

internal sealed class HostPreparation(
    IRegistryAccess registry,
    IFirewallRules firewall,
    IPrivilegeChecker privileges,
    IUserRegistryAccess users,
    INetworkProfiles networks,
    ILocalAccounts accounts,
    IServiceControl services,
    IPowerSettings power,
    ILogger<HostPreparation> logger) : IHostPreparation
{
    public const string RdpServiceName = "TermService";
    private const int ServiceDisabled = 4;
    private const string LanguageBarSnapshotPrefix = nameof(ReadinessCheckId.LanguageBarHidden) + ":";
    private const string NetworkSnapshotPrefix = nameof(ReadinessCheckId.NetworkProfile) + ":";
    private const string RemoteUsersSnapshotPrefix = nameof(ReadinessCheckId.RemoteDesktopUsers) + ":";

    private static readonly string[] SupportedEditionPrefixes = ["Professional", "Enterprise", "Education", "Server", "IoTEnterprise"];

    /// <summary>A DWORD setting the preparation can enforce. <paramref name="Forbidden"/>: only that value is wrong.</summary>
    private sealed record DwordSetting(ReadinessCheckId Id, string Description, string Path, string Name, int Expected, bool AbsentIsOk,
        int? Forbidden = null);

    /// <summary>What <see cref="Prepare"/> enforces. Built on each use so the descriptions follow the current language.</summary>
    private static DwordSetting[] Settings =>
    [
        new(ReadinessCheckId.RdpEnabled, Strings.T("host.readiness.rdpEnabled"),
            RegistryPaths.TerminalServer, "fDenyTSConnections", Expected: 0, AbsentIsOk: false),
        new(ReadinessCheckId.RdpPolicy, Strings.T("host.readiness.rdpPolicy"),
            RegistryPaths.TerminalServicesPolicy, "fDenyTSConnections", Expected: 0, AbsentIsOk: true),
        new(ReadinessCheckId.RdpService, Strings.T("host.readiness.rdpService"),
            RegistryPaths.TermService, "Start", Expected: 3, AbsentIsOk: true, Forbidden: ServiceDisabled),
        new(ReadinessCheckId.Nla, Strings.T("host.readiness.nla"),
            RegistryPaths.RdpTcp, "UserAuthentication", Expected: 1, AbsentIsOk: false),
        new(ReadinessCheckId.AllowListEnforced, Strings.T("host.readiness.allowListEnforced"),
            RegistryPaths.AllowList, "fDisabledAllowList", Expected: 0, AbsentIsOk: true),
    ];

    /// <summary>Only changed on request (<see cref="Fix"/>): it changes how the user signs in at the PC itself.</summary>
    private static DwordSetting PasswordLess =>
        new(ReadinessCheckId.MicrosoftAccount, Strings.T("host.readiness.microsoftAccount"),
            RegistryPaths.PasswordLess, "DevicePasswordLessBuildVersion", Expected: 0, AbsentIsOk: true, Forbidden: 2);

    private readonly SnapshotStore _snapshot = new(registry);

    public IReadOnlyList<ReadinessCheck> Check()
    {
        var checks = new List<ReadinessCheck> { CheckEdition(out _) };
        foreach (var setting in Settings)
        {
            checks.Add(setting.Id == ReadinessCheckId.RdpService ? CheckRdpService(setting) : CheckSetting(setting));
        }

        checks.Add(CheckFirewall());
        checks.Add(CheckLanguageBar());
        checks.Add(CheckNetwork());
        checks.AddRange(CheckConsoleUser());
        checks.Add(CheckSleep());
        return checks;
    }

    public IReadOnlyList<ReadinessCheck> Prepare()
    {
        EnsureAdmin();
        EnsureSupported();

        foreach (var setting in Settings)
        {
            Apply(setting);
        }

        StartRdpServiceIfNeeded();
        EnableFirewallRules();
        HideLanguageBars();
        return Check();
    }

    public IReadOnlyList<ReadinessCheck> Fix(ReadinessCheckId id)
    {
        EnsureAdmin();
        EnsureSupported();

        switch (id)
        {
            case ReadinessCheckId.RdpService:
                Apply(Settings.Single(s => s.Id == id));
                StartRdpServiceIfNeeded();
                break;
            case ReadinessCheckId.RdpEnabled or ReadinessCheckId.RdpPolicy or ReadinessCheckId.Nla or ReadinessCheckId.AllowListEnforced:
                Apply(Settings.Single(s => s.Id == id));
                break;
            case ReadinessCheckId.FirewallRdp:
                EnableFirewallRules();
                break;
            case ReadinessCheckId.LanguageBarHidden:
                HideLanguageBars();
                break;
            case ReadinessCheckId.NetworkProfile:
                MakePublicNetworksPrivate();
                break;
            case ReadinessCheckId.RemoteDesktopUsers:
                AllowConsoleUserRemotely();
                break;
            case ReadinessCheckId.MicrosoftAccount:
                Apply(PasswordLess);
                break;
            default:
                throw new InvalidOperationException(Strings.T("host.readiness.cannotFix"));
        }

        return Check();
    }

    public IReadOnlyList<ReadinessCheck> SetAccountPassword(string password)
    {
        EnsureAdmin();
        if (string.IsNullOrEmpty(password))
        {
            throw new AppValidationException("password", Strings.T("host.accounts.passwordRequired"));
        }

        var account = accounts.ConsoleUser() ?? throw new InvalidOperationException(Strings.T("host.accounts.noConsoleUser"));
        if (!account.IsLocal || account.MicrosoftAccount is not null)
        {
            throw new InvalidOperationException(Strings.T("host.accounts.notLocal"));
        }

        accounts.SetPassword(account, password);
        logger.LogInformation("Fix {Check}: password set for {User}", ReadinessCheckId.AccountPassword, account.UserName);
        return Check();
    }

    public IReadOnlyList<ReadinessCheck> Revert()
    {
        EnsureAdmin();
        EnsureSupported();

        if (!_snapshot.Exists)
        {
            logger.LogInformation("Revert: no preparation snapshot; nothing to do");
            return Check();
        }

        foreach (var setting in Settings.Append(PasswordLess))
        {
            var previous = _snapshot.ReadDword(setting.Id);
            if (previous is null)
            {
                continue;
            }

            if (previous.Existed)
            {
                registry.SetValue(setting.Path, setting.Name, previous.Value, RegistryValueKind.DWord);
            }
            else
            {
                registry.DeleteValue(setting.Path, setting.Name);
            }

            logger.LogInformation("Revert {Check}: {Path}\\{Name} -> {Value}",
                setting.Id, setting.Path, setting.Name, previous.Existed ? previous.Value : "(absent)");
        }

        foreach (var rule in _snapshot.ReadEnabledRules())
        {
            firewall.SetEnabled(rule, false);
            logger.LogInformation("Revert {Check}: disabled firewall rule {Rule}", ReadinessCheckId.FirewallRdp, rule);
        }

        RevertLanguageBars();
        RevertNetworks();
        RevertRemoteUsers();

        _snapshot.Clear();
        return Check();
    }

    private void Apply(DwordSetting setting)
    {
        var current = ReadDword(setting);
        if (IsOk(setting, current))
        {
            return;
        }

        _snapshot.RecordDword(setting.Id, current);
        registry.SetValue(setting.Path, setting.Name, setting.Expected, RegistryValueKind.DWord);
        logger.LogInformation("Prepare {Check}: {Path}\\{Name} {Previous} -> {Value}",
            setting.Id, setting.Path, setting.Name, current.Existed ? current.Value : "(absent)", setting.Expected);
    }

    /// <summary>With Remote Desktop on, its service must run for the PC to accept connections.</summary>
    private void StartRdpServiceIfNeeded()
    {
        if (!IsRdpOn() || services.IsRunning(RdpServiceName))
        {
            return;
        }

        services.Start(RdpServiceName);
        logger.LogInformation("Prepare {Check}: started {Service}", ReadinessCheckId.RdpService, RdpServiceName);
    }

    private void EnableFirewallRules()
    {
        var disabled = firewall.GetRdpRules().Where(r => !r.Enabled).Select(r => r.Name).ToList();
        if (disabled.Count == 0)
        {
            return;
        }

        _snapshot.RecordEnabledRules(disabled);
        foreach (var rule in disabled)
        {
            firewall.SetEnabled(rule, true);
            logger.LogInformation("Prepare {Check}: enabled firewall rule {Rule}", ReadinessCheckId.FirewallRdp, rule);
        }
    }

    private void HideLanguageBars()
    {
        foreach (var sid in users.LoadedUserSids())
        {
            var current = ReadLanguageBar(sid);
            if (current is { Existed: true, Value: LanguageBarSettings.Hidden })
            {
                continue;
            }

            _snapshot.RecordDword(LanguageBarSnapshotPrefix + sid, current);
            users.SetValue(sid, LanguageBarSettings.Path, LanguageBarSettings.Name, LanguageBarSettings.Hidden, RegistryValueKind.DWord);
            logger.LogInformation("Prepare {Check}: language bar hidden for {Sid}", ReadinessCheckId.LanguageBarHidden, sid);
        }
    }

    private void MakePublicNetworksPrivate()
    {
        foreach (var network in networks.Connected().Where(n => n.Category == NetworkCategory.Public))
        {
            _snapshot.RecordDword(NetworkSnapshotPrefix + network.Id.ToString("D"), new DwordSnapshot(true, (int)network.Category));
            networks.SetCategory(network.Id, NetworkCategory.Private);
            logger.LogInformation("Fix {Check}: network {Network} set to Private", ReadinessCheckId.NetworkProfile, network.Name);
        }
    }

    private void AllowConsoleUserRemotely()
    {
        var account = accounts.ConsoleUser() ?? throw new InvalidOperationException(Strings.T("host.accounts.noConsoleUser"));
        if (account.CanSignInRemotely)
        {
            return;
        }

        // Not a member before: revert removes it again.
        _snapshot.RecordDword(RemoteUsersSnapshotPrefix + account.Sid, new DwordSnapshot(false, 0));
        accounts.AddToRemoteDesktopUsers(account.Sid);
        logger.LogInformation("Fix {Check}: {User} added to Remote Desktop Users", ReadinessCheckId.RemoteDesktopUsers, account.UserName);
    }

    private void RevertLanguageBars()
    {
        var loaded = users.LoadedUserSids().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _snapshot.NamesWithPrefix(LanguageBarSnapshotPrefix))
        {
            var sid = name[LanguageBarSnapshotPrefix.Length..];
            var previous = _snapshot.ReadDword(name);
            if (previous is null || !loaded.Contains(sid))
            {
                logger.LogWarning("Revert {Check}: profile {Sid} not loaded; language bar left as is", ReadinessCheckId.LanguageBarHidden, sid);
                continue;
            }

            if (previous.Existed)
            {
                users.SetValue(sid, LanguageBarSettings.Path, LanguageBarSettings.Name, previous.Value, RegistryValueKind.DWord);
            }
            else
            {
                users.DeleteValue(sid, LanguageBarSettings.Path, LanguageBarSettings.Name);
            }

            logger.LogInformation("Revert {Check}: language bar restored for {Sid}", ReadinessCheckId.LanguageBarHidden, sid);
        }
    }

    private void RevertNetworks()
    {
        var connected = networks.Connected().Select(n => n.Id).ToHashSet();
        foreach (var name in _snapshot.NamesWithPrefix(NetworkSnapshotPrefix))
        {
            var previous = _snapshot.ReadDword(name);
            if (previous is not { Existed: true } || !Guid.TryParse(name[NetworkSnapshotPrefix.Length..], out var id) || !connected.Contains(id))
            {
                logger.LogWarning("Revert {Check}: network {Network} not connected; category left as is", ReadinessCheckId.NetworkProfile, name);
                continue;
            }

            networks.SetCategory(id, (NetworkCategory)previous.Value);
            logger.LogInformation("Revert {Check}: network {Network} -> {Category}", ReadinessCheckId.NetworkProfile, id, (NetworkCategory)previous.Value);
        }
    }

    private void RevertRemoteUsers()
    {
        foreach (var name in _snapshot.NamesWithPrefix(RemoteUsersSnapshotPrefix))
        {
            var sid = name[RemoteUsersSnapshotPrefix.Length..];
            accounts.RemoveFromRemoteDesktopUsers(sid);
            logger.LogInformation("Revert {Check}: {Sid} removed from Remote Desktop Users", ReadinessCheckId.RemoteDesktopUsers, sid);
        }
    }

    private ReadinessCheck CheckEdition(out bool supported)
    {
        var edition = registry.GetValue(RegistryPaths.CurrentVersion, "EditionID") as string ?? Strings.T("host.readiness.editionUnknown");
        supported = SupportedEditionPrefixes.Any(p => edition.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        return new ReadinessCheck(ReadinessCheckId.Edition, Strings.T("host.readiness.edition"),
            supported ? ReadinessState.Ok : ReadinessState.NotSupported, edition, Strings.T("host.readiness.editionExpected"));
    }

    private ReadinessCheck CheckSetting(DwordSetting setting)
    {
        var current = ReadDword(setting);
        return new ReadinessCheck(setting.Id, setting.Description,
            IsOk(setting, current) ? ReadinessState.Ok : ReadinessState.Pending,
            current.Existed ? current.Value.ToString() : Strings.T("host.readiness.absent"),
            setting.Expected.ToString(), ReadinessFix.Automatic);
    }

    private ReadinessCheck CheckRdpService(DwordSetting setting)
    {
        var disabled = !IsOk(setting, ReadDword(setting));
        var running = services.IsRunning(RdpServiceName);
        var ok = !disabled && (running || !IsRdpOn());
        var current = disabled ? Strings.T("host.readiness.rdpServiceDisabled")
            : running ? Strings.T("host.readiness.rdpServiceRunning")
            : Strings.T("host.readiness.rdpServiceStopped");
        return new ReadinessCheck(setting.Id, setting.Description, ok ? ReadinessState.Ok : ReadinessState.Pending,
            current, Strings.T("host.readiness.rdpServiceRunning"), ReadinessFix.Automatic, "ms-settings:remotedesktop");
    }

    private ReadinessCheck CheckFirewall()
    {
        var rules = firewall.GetRdpRules();
        var enabled = rules.Count(r => r.Enabled);
        var ok = rules.Count > 0 && enabled == rules.Count;
        var current = rules.Count == 0
            ? Strings.T("host.readiness.firewallNoRules")
            : Strings.T("host.readiness.firewallEnabledRules", enabled, rules.Count);
        return new ReadinessCheck(ReadinessCheckId.FirewallRdp, Strings.T("host.readiness.firewall"),
            ok ? ReadinessState.Ok : ReadinessState.Pending, current, Strings.T("host.readiness.firewallExpected"), ReadinessFix.Automatic);
    }

    private ReadinessCheck CheckLanguageBar()
    {
        var sids = users.LoadedUserSids();
        var hidden = sids.Count(s => ReadLanguageBar(s) is { Existed: true, Value: LanguageBarSettings.Hidden });
        var current = sids.Count == 0
            ? Strings.T("host.readiness.languageBarNoProfiles")
            : Strings.T("host.readiness.languageBarProfiles", hidden, sids.Count);
        return new ReadinessCheck(ReadinessCheckId.LanguageBarHidden, Strings.T("host.readiness.languageBar"),
            hidden == sids.Count ? ReadinessState.Ok : ReadinessState.Pending, current, Strings.T("host.readiness.languageBarExpected"),
            ReadinessFix.Automatic);
    }

    private ReadinessCheck CheckNetwork()
    {
        var connected = networks.Connected();
        var publicOnes = connected.Where(n => n.Category == NetworkCategory.Public).Select(n => n.Name).ToList();
        var current = connected.Count == 0 ? Strings.T("host.readiness.networkNone")
            : publicOnes.Count > 0 ? Strings.T("host.readiness.networkPublic", string.Join(", ", publicOnes))
            : string.Join(", ", connected.Select(n => n.Name));
        return new ReadinessCheck(ReadinessCheckId.NetworkProfile, Strings.T("host.readiness.network"),
            publicOnes.Count == 0 ? ReadinessState.Ok : ReadinessState.Pending, current, Strings.T("host.readiness.networkExpected"),
            ReadinessFix.Automatic, "ms-settings:network-status");
    }

    /// <summary>Checks about the account signed in at the PC (none when nobody is).</summary>
    private IEnumerable<ReadinessCheck> CheckConsoleUser()
    {
        var account = accounts.ConsoleUser();
        if (account is null)
        {
            yield break;
        }

        if (account is { IsLocal: true, MicrosoftAccount: null })
        {
            var status = accounts.GetPasswordStatus(account);
            yield return new ReadinessCheck(ReadinessCheckId.AccountPassword, Strings.T("host.readiness.accountPassword", account.UserName),
                status == PasswordStatus.Blank ? ReadinessState.Pending : ReadinessState.Ok,
                status switch
                {
                    PasswordStatus.Blank => Strings.T("host.readiness.passwordBlank"),
                    PasswordStatus.Set => Strings.T("host.readiness.passwordSet"),
                    _ => Strings.T("host.readiness.passwordUnknown"),
                },
                Strings.T("host.readiness.passwordExpected"), ReadinessFix.SetPassword, "ms-settings:signinoptions");
        }

        yield return new ReadinessCheck(ReadinessCheckId.RemoteDesktopUsers, Strings.T("host.readiness.remoteUsers", account.UserName),
            account.CanSignInRemotely ? ReadinessState.Ok : ReadinessState.Pending,
            account.CanSignInRemotely ? Strings.T("host.readiness.remoteUsersYes") : Strings.T("host.readiness.remoteUsersNo"),
            Strings.T("host.readiness.remoteUsersExpected"), ReadinessFix.Automatic);

        if (account.MicrosoftAccount is { } email)
        {
            var passwordless = !IsOk(PasswordLess, ReadDword(PasswordLess));
            yield return new ReadinessCheck(ReadinessCheckId.MicrosoftAccount, PasswordLess.Description,
                passwordless ? ReadinessState.Pending : ReadinessState.Advice,
                passwordless ? Strings.T("host.readiness.passwordlessOn", email) : email,
                Strings.T("host.readiness.passwordlessExpected"),
                passwordless ? ReadinessFix.Automatic : ReadinessFix.None, "ms-settings:signinoptions");
        }
    }

    private ReadinessCheck CheckSleep()
    {
        var after = power.SleepAfter();
        return new ReadinessCheck(ReadinessCheckId.Sleep, Strings.T("host.readiness.sleep"),
            after is null ? ReadinessState.Ok : ReadinessState.Advice,
            after is { } t ? Strings.T("host.readiness.sleepAfter", (int)Math.Round(t.TotalMinutes)) : Strings.T("host.readiness.sleepNever"),
            Strings.T("host.readiness.sleepExpected"), ReadinessFix.OpenSettings, "ms-settings:powersleep");
    }

    private bool IsRdpOn() =>
        Settings.Where(s => s.Id is ReadinessCheckId.RdpEnabled or ReadinessCheckId.RdpPolicy).All(s => IsOk(s, ReadDword(s)));

    private DwordSnapshot ReadLanguageBar(string sid) =>
        users.GetValue(sid, LanguageBarSettings.Path, LanguageBarSettings.Name) is int value
            ? new DwordSnapshot(true, value)
            : new DwordSnapshot(false, 0);

    private DwordSnapshot ReadDword(DwordSetting setting) =>
        registry.GetValue(setting.Path, setting.Name) is int value ? new DwordSnapshot(true, value) : new DwordSnapshot(false, 0);

    private static bool IsOk(DwordSetting setting, DwordSnapshot current) =>
        setting.Forbidden is { } forbidden
            ? !current.Existed || current.Value != forbidden
            : current.Existed ? current.Value == setting.Expected : setting.AbsentIsOk;

    private void EnsureAdmin()
    {
        if (!privileges.IsAdministrator())
        {
            throw new AdminRequiredException();
        }
    }

    private void EnsureSupported()
    {
        var edition = CheckEdition(out var supported);
        if (!supported)
        {
            throw new HostNotSupportedException(edition.CurrentValue);
        }
    }
}
