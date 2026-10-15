using System.Runtime.InteropServices;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

/// <summary>
/// Uses the Network List Manager COM API (the same as the Windows network settings page). Declared interfaces
/// instead of late binding: the network id is a GUID, which IDispatch cannot return.
/// </summary>
internal sealed class WindowsNetworkProfiles : INetworkProfiles
{
    private static readonly Guid NetworkListManagerClsid = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
    private const int EnumConnected = 1; // NLM_ENUM_NETWORK_CONNECTED

    public IReadOnlyList<NetworkProfile> Connected()
    {
        var result = new List<NetworkProfile>();
        var networks = CreateManager().GetNetworks(EnumConnected);
        while (networks.Next(1, out var network, out var fetched) == 0 && fetched == 1)
        {
            result.Add(new NetworkProfile(network.GetNetworkId(), network.GetName(), (NetworkCategory)network.GetCategory()));
        }

        return result;
    }

    public void SetCategory(Guid networkId, NetworkCategory category) =>
        CreateManager().GetNetwork(networkId).SetCategory((int)category);

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

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetNetworkConnections();

        void GetTimeCreatedAndConnected(out uint createdLow, out uint createdHigh, out uint connectedLow, out uint connectedHigh);

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnectedToInternet();

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnected();

        int GetConnectivity();

        int GetCategory();

        void SetCategory(int category);
    }
}
