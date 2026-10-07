namespace WRApperture.Host.Core.Models;

public enum ReadinessCheckId
{
    Edition,
    RdpEnabled,
    Nla,
    AllowListEnforced,
    FirewallRdp,
}

public enum ReadinessState
{
    Ok,
    Pending,
    NotSupported,
}

public sealed record ReadinessCheck(
    ReadinessCheckId Id,
    string Description,
    ReadinessState State,
    string CurrentValue,
    string ExpectedValue);
