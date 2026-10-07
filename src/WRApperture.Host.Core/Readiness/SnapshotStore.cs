using System.Globalization;
using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Publishing;

namespace WRApperture.Host.Core.Readiness;

/// <summary>Previous DWORD state of a registry value: whether it existed and what it held.</summary>
internal sealed record DwordSnapshot(bool Existed, int Value);

/// <summary>
/// Records pre-preparation state under HKLM\SOFTWARE\WRApperture\PreparationSnapshot.
/// The first recorded value for a check is never overwritten, so repeated prepares keep the true original.
/// </summary>
internal sealed class SnapshotStore(IRegistryAccess registry)
{
    private const string Path = RegistryPaths.PreparationSnapshot;

    public bool Exists => registry.KeyExists(Path);

    public void RecordDword(ReadinessCheckId id, DwordSnapshot snapshot) => RecordDword(id.ToString(), snapshot);

    public DwordSnapshot? ReadDword(ReadinessCheckId id) => ReadDword(id.ToString());

    /// <summary>Snapshot entries are named "&lt;check&gt;" or "&lt;check&gt;:&lt;qualifier&gt;" (e.g. per-user SID).</summary>
    public void RecordDword(string name, DwordSnapshot snapshot)
    {
        if (registry.GetValue(Path, name) is not null)
        {
            return;
        }

        var text = snapshot.Existed ? $"1;DWord;{snapshot.Value.ToString(CultureInfo.InvariantCulture)}" : "0;;";
        registry.SetValue(Path, name, text, RegistryValueKind.String);
    }

    public IReadOnlyList<string> NamesWithPrefix(string prefix) =>
        registry.GetValueNames(Path).Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

    public DwordSnapshot? ReadDword(string name)
    {
        if (registry.GetValue(Path, name) is not string text)
        {
            return null;
        }

        var parts = text.Split(';');
        return parts[0] == "1" && parts.Length == 3 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var v)
            ? new DwordSnapshot(true, v)
            : new DwordSnapshot(false, 0);
    }

    /// <summary>Firewall rules that were disabled and got enabled by the tool.</summary>
    public void RecordEnabledRules(IReadOnlyCollection<string> ruleNames)
    {
        if (ruleNames.Count == 0 || registry.GetValue(Path, nameof(ReadinessCheckId.FirewallRdp)) is not null)
        {
            return;
        }

        registry.SetValue(Path, nameof(ReadinessCheckId.FirewallRdp), ruleNames.ToArray(), RegistryValueKind.MultiString);
    }

    public IReadOnlyList<string> ReadEnabledRules() =>
        registry.GetValue(Path, nameof(ReadinessCheckId.FirewallRdp)) as string[] ?? [];

    public void Clear()
    {
        if (Exists)
        {
            registry.DeleteKey(Path);
        }
    }
}
