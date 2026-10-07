namespace WRApperture.Host.Core.Abstractions;

public sealed record FirewallRule(string Name, bool Enabled);

/// <summary>Remote Desktop firewall rules, located by their language-neutral group.</summary>
public interface IFirewallRules
{
    IReadOnlyList<FirewallRule> GetRdpRules();
    void SetEnabled(string name, bool enabled);
}
