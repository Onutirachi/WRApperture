namespace WRApperture.Host.Core.Abstractions;

/// <summary>Values of NLM_NETWORK_CATEGORY.</summary>
public enum NetworkCategory
{
    Public = 0,
    Private = 1,
    Domain = 2,
}

/// <param name="VpnName">The VPN behind this network ("Fortinet SSL VPN"); null for local networks.</param>
public sealed record NetworkProfile(Guid Id, string Name, NetworkCategory Category, string? VpnName = null)
{
    /// <summary>Other PCs never come in through a VPN, so its category does not matter for remote access.</summary>
    public bool IsVpn => VpnName is not null;
}

/// <summary>The networks this PC is connected to and their Windows category (Network List Manager).</summary>
public interface INetworkProfiles
{
    IReadOnlyList<NetworkProfile> Connected();

    /// <summary>Requires admin. Domain networks cannot be changed.</summary>
    void SetCategory(Guid networkId, NetworkCategory category);
}
