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
    ILogger<HostPreparation> logger) : IHostPreparation
{
    private const string LanguageBarSnapshotPrefix = nameof(ReadinessCheckId.LanguageBarHidden) + ":";

    private static readonly string[] SupportedEditionPrefixes = ["Professional", "Enterprise", "Education", "Server", "IoTEnterprise"];

    /// <summary>A DWORD setting the preparation can enforce.</summary>
    private sealed record DwordSetting(ReadinessCheckId Id, string Description, string Path, string Name, int Expected, bool AbsentIsOk);

    /// <summary>Built on each use so the descriptions follow the current language.</summary>
    private static DwordSetting[] Settings =>
    [
        new(ReadinessCheckId.RdpEnabled, Strings.T("host.readiness.rdpEnabled"),
            RegistryPaths.TerminalServer, "fDenyTSConnections", Expected: 0, AbsentIsOk: false),
        new(ReadinessCheckId.Nla, Strings.T("host.readiness.nla"),
            RegistryPaths.RdpTcp, "UserAuthentication", Expected: 1, AbsentIsOk: false),
        new(ReadinessCheckId.AllowListEnforced, Strings.T("host.readiness.allowListEnforced"),
            RegistryPaths.AllowList, "fDisabledAllowList", Expected: 0, AbsentIsOk: true),
    ];

    private readonly SnapshotStore _snapshot = new(registry);

    public IReadOnlyList<ReadinessCheck> Check()
    {
        var checks = new List<ReadinessCheck> { CheckEdition(out _) };
        checks.AddRange(Settings.Select(CheckSetting));
        checks.Add(CheckFirewall());
        checks.Add(CheckLanguageBar());
        return checks;
    }

    public IReadOnlyList<ReadinessCheck> Prepare()
    {
        EnsureAdmin();
        EnsureSupported();

        foreach (var setting in Settings)
        {
            var current = ReadDword(setting);
            if (IsOk(setting, current))
            {
                continue;
            }

            _snapshot.RecordDword(setting.Id, current);
            registry.SetValue(setting.Path, setting.Name, setting.Expected, RegistryValueKind.DWord);
            logger.LogInformation("Prepare {Check}: {Path}\\{Name} {Previous} -> {Value}",
                setting.Id, setting.Path, setting.Name, current.Existed ? current.Value : "(absent)", setting.Expected);
        }

        var disabled = firewall.GetRdpRules().Where(r => !r.Enabled).Select(r => r.Name).ToList();
        if (disabled.Count > 0)
        {
            _snapshot.RecordEnabledRules(disabled);
            foreach (var rule in disabled)
            {
                firewall.SetEnabled(rule, true);
                logger.LogInformation("Prepare {Check}: enabled firewall rule {Rule}", ReadinessCheckId.FirewallRdp, rule);
            }
        }

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

        foreach (var setting in Settings)
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

        _snapshot.Clear();
        return Check();
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
            setting.Expected.ToString());
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
            ok ? ReadinessState.Ok : ReadinessState.Pending, current, Strings.T("host.readiness.firewallExpected"));
    }

    private ReadinessCheck CheckLanguageBar()
    {
        var sids = users.LoadedUserSids();
        var hidden = sids.Count(s => ReadLanguageBar(s) is { Existed: true, Value: LanguageBarSettings.Hidden });
        var current = sids.Count == 0
            ? Strings.T("host.readiness.languageBarNoProfiles")
            : Strings.T("host.readiness.languageBarProfiles", hidden, sids.Count);
        return new ReadinessCheck(ReadinessCheckId.LanguageBarHidden, Strings.T("host.readiness.languageBar"),
            hidden == sids.Count ? ReadinessState.Ok : ReadinessState.Pending, current, Strings.T("host.readiness.languageBarExpected"));
    }

    private DwordSnapshot ReadLanguageBar(string sid) =>
        users.GetValue(sid, LanguageBarSettings.Path, LanguageBarSettings.Name) is int value
            ? new DwordSnapshot(true, value)
            : new DwordSnapshot(false, 0);

    private DwordSnapshot ReadDword(DwordSetting setting) =>
        registry.GetValue(setting.Path, setting.Name) is int value ? new DwordSnapshot(true, value) : new DwordSnapshot(false, 0);

    private static bool IsOk(DwordSetting setting, DwordSnapshot current) =>
        current.Existed ? current.Value == setting.Expected : setting.AbsentIsOk;

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
