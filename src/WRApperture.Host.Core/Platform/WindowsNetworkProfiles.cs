using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

/// <summary>
/// Uses the Network List Manager COM API (the same as the Windows network settings page). Declared interfaces
/// instead of late binding: the network id is a GUID, which IDispatch cannot return.
/// </summary>
internal sealed class WindowsNetworkProfiles : INetworkProfiles
{
    private static readonly string[] VpnWords = ["VPN", "WireGuard", "TAP-Windows"];
    private static readonly Guid NetworkListManagerClsid = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
    private const int EnumConnected = 1; // NLM_ENUM_NETWORK_CONNECTED

    public IReadOnlyList<NetworkProfile> Connected()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces();
        var result = new List<NetworkProfile>();
        var networks = CreateManager().GetNetworks(EnumConnected);
        while (networks.Next(1, out var network, out var fetched) == 0 && fetched == 1)
        {
            result.Add(new NetworkProfile(network.GetNetworkId(), network.GetName(), (NetworkCategory)network.GetCategory(),
                VpnBehind(network, adapters)));
        }

        return result;
    }

    public void SetCategory(Guid networkId, NetworkCategory category) =>
        CreateManager().GetNetwork(networkId).SetCategory((int)category);

    /// <summary>The name of the VPN whose adapter carries this network, or null.</summary>
    private static string? VpnBehind(INetwork network, NetworkInterface[] adapters)
    {
        try
        {
            var connections = network.GetNetworkConnections();
            while (connections.Next(1, out var connection, out var fetched) == 0 && fetched == 1)
            {
                var id = connection.GetAdapterId();
                var adapter = adapters.FirstOrDefault(a => Guid.TryParse(a.Id, out var adapterId) && adapterId == id);
                if (adapter is not null && VpnName(adapter) is { } name)
                {
                    return name;
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NetworkInformationException)
        {
            // Not knowing makes it a local network, which is what the checks assumed before.
        }

        return null;
    }

    /// <summary>
    /// VPN adapters: dial-up style (PPP, e.g. Windows' own VPN connections, shown by their connection name) and virtual
    /// adapters that say so ("Fortinet SSL VPN Virtual Ethernet Adapter" → "Fortinet SSL VPN", WireGuard, OpenVPN's
    /// TAP-Windows). Not the "Tunnel" type as such: Windows' own Teredo, 6to4 and IP-HTTPS adapters have it too.
    /// </summary>
    internal static string? VpnName(NetworkInterface adapter) => VpnName(adapter.NetworkInterfaceType, adapter.Name, adapter.Description);

    internal static string? VpnName(NetworkInterfaceType type, string name, string description)
    {
        if (type == NetworkInterfaceType.Ppp)
        {
            return name;
        }

        if (!VpnWords.Any(word => description.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        foreach (var suffix in new[] { " Virtual Ethernet Adapter", " Virtual Network Adapter", " Network Adapter", " Adapter" })
        {
            if (description.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return description[..^suffix.Length].Trim();
            }
        }

        return description.Trim();
    }

    private static INetworkListManager CreateManager() =>
        (INetworkListManager)Activator.CreateInstance(Type.GetTypeFromCLSID(NetworkListManagerClsid, throwOnError: true)!)!;

    // Only the leading methods are declared: the vtable order is what matters.
    [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkListManager
    {
        IEnumNetworks GetNetworks(int flags);

        INetwork GetNetwork(Guid networkId);
    }

    [ComImport, Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IEnumNetworks
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetNewEnum();

        [PreserveSig]
        int Next(int count, out INetwork network, out int fetched);
    }

    [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetwork
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetName();

        void SetName([MarshalAs(UnmanagedType.BStr)] string name);

        [return: MarshalAs(UnmanagedType.BStr)]
        string GetDescription();

        void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);

        Guid GetNetworkId();

        int GetDomainType();

        IEnumNetworkConnections GetNetworkConnections();

        void GetTimeCreatedAndConnected(out uint createdLow, out uint createdHigh, out uint connectedLow, out uint connectedHigh);

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnectedToInternet();

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnected();

        int GetConnectivity();

        int GetCategory();

        void SetCategory(int category);
    }

    [ComImport, Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IEnumNetworkConnections
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetNewEnum();

        [PreserveSig]
        int Next(int count, out INetworkConnection connection, out int fetched);
    }

    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkConnection
    {
        INetwork GetNetwork();

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnectedToInternet();

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnected();

        int GetConnectivity();

        Guid GetConnectionId();

        Guid GetAdapterId();

        int GetDomainType();
    }
}
