using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

/// <summary>
/// Uses the HNetCfg.FwPolicy2 COM API. Rules are matched by the indirect group
/// string, which is identical in every Windows display language. Only the inbound
/// rules on port 3389 are considered (the "Shadow" rule is left alone).
/// </summary>
internal sealed class ComFirewallRules : IFirewallRules
{
    internal const string RemoteDesktopGroup = "@FirewallAPI.dll,-28752";
    private const int DirectionInbound = 1;
    private const string RdpPort = "3389";

    public IReadOnlyList<FirewallRule> GetRdpRules()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in EnumerateRdpRules())
        {
            string name = rule.Name;
            bool enabled = rule.Enabled;
            // A name can appear once per profile; it counts as enabled only if every copy is.
            result[name] = result.TryGetValue(name, out var prev) ? prev && enabled : enabled;
        }

        return result.Select(kv => new FirewallRule(kv.Key, kv.Value)).ToList();
    }

    public void SetEnabled(string name, bool enabled)
    {
        foreach (var rule in EnumerateRdpRules())
        {
            if (string.Equals((string)rule.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                rule.Enabled = enabled;
            }
        }
    }

    private static IEnumerable<dynamic> EnumerateRdpRules()
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!;
        dynamic policy = Activator.CreateInstance(type)!;
        foreach (dynamic rule in policy.Rules)
        {
            if (rule.Grouping is string group &&
                string.Equals(group, RemoteDesktopGroup, StringComparison.OrdinalIgnoreCase) &&
                (int)rule.Direction == DirectionInbound &&
                rule.LocalPorts is string ports && ports == RdpPort)
            {
                yield return rule;
            }
        }
    }
}
