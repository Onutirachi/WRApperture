using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Publishing;

namespace WRApperture.Host.Core.Readiness;

internal sealed class HostPreparation(
    IRegistryAccess registry,
    IFirewallRules firewall,
    IPrivilegeChecker privileges,
    ILogger<HostPreparation> logger) : IHostPreparation
{
    private static readonly string[] SupportedEditionPrefixes = ["Professional", "Enterprise", "Education", "Server", "IoTEnterprise"];

    /// <summary>A DWORD setting the preparation can enforce.</summary>
    private sealed record DwordSetting(ReadinessCheckId Id, string Description, string Path, string Name, int Expected, bool AbsentIsOk);

    private static readonly DwordSetting[] Settings =
    [
        new(ReadinessCheckId.RdpEnabled, "Área de Trabalho Remota habilitada",
            RegistryPaths.TerminalServer, "fDenyTSConnections", Expected: 0, AbsentIsOk: false),
        new(ReadinessCheckId.Nla, "Autenticação em nível de rede (NLA) ativa",
            RegistryPaths.RdpTcp, "UserAuthentication", Expected: 1, AbsentIsOk: false),
        new(ReadinessCheckId.AllowListEnforced, "Somente apps publicados podem ser abertos remotamente",
            RegistryPaths.AllowList, "fDisabledAllowList", Expected: 0, AbsentIsOk: true),
    ];

    private readonly SnapshotStore _snapshot = new(registry);

    public IReadOnlyList<ReadinessCheck> Check()
    {
        var checks = new List<ReadinessCheck> { CheckEdition(out _) };
        checks.AddRange(Settings.Select(CheckSetting));
        checks.Add(CheckFirewall());
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

        _snapshot.Clear();
        return Check();
    }

    private ReadinessCheck CheckEdition(out bool supported)
    {
        var edition = registry.GetValue(RegistryPaths.CurrentVersion, "EditionID") as string ?? "desconhecida";
        supported = SupportedEditionPrefixes.Any(p => edition.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        return new ReadinessCheck(ReadinessCheckId.Edition, "Edição do Windows suporta RemoteApp",
            supported ? ReadinessState.Ok : ReadinessState.NotSupported, edition, "Pro, Enterprise ou Education");
    }

    private ReadinessCheck CheckSetting(DwordSetting setting)
    {
        var current = ReadDword(setting);
        return new ReadinessCheck(setting.Id, setting.Description,
            IsOk(setting, current) ? ReadinessState.Ok : ReadinessState.Pending,
            current.Existed ? current.Value.ToString() : "(ausente)",
            setting.Expected.ToString());
    }

    private ReadinessCheck CheckFirewall()
    {
        var rules = firewall.GetRdpRules();
        var enabled = rules.Count(r => r.Enabled);
        var ok = rules.Count > 0 && enabled == rules.Count;
        var current = rules.Count == 0 ? "nenhuma regra de RDP encontrada" : $"{enabled}/{rules.Count} regras ativas";
        return new ReadinessCheck(ReadinessCheckId.FirewallRdp, "Firewall libera a Área de Trabalho Remota (porta 3389)",
            ok ? ReadinessState.Ok : ReadinessState.Pending, current, "todas ativas");
    }

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
