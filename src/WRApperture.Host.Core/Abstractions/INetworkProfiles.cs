namespace WRApperture.Host.Core.Abstractions;

/// <summary>Values of NLM_NETWORK_CATEGORY.</summary>
public enum NetworkCategory
{
    Public = 0,
    Private = 1,
    Domain = 2,
}

public sealed record NetworkProfile(Guid Id, string Name, NetworkCategory Category);

/// <summary>The networks this PC is connected to and their Windows category (Network List Manager).</summary>
public interface INetworkProfiles
{
    IReadOnlyList<NetworkProfile> Connected();

    /// <summary>Requires admin. Domain networks cannot be changed.</summary>
    void SetCategory(Guid networkId, NetworkCategory category);
}
