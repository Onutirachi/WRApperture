using WRApperture.Host.Core.Sessions;

namespace WRApperture.Host.Core.Tests.Sessions;

public class VpnAdapterTests
{
    [Theory]
    [InlineData(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "Ethernet 3", "Fortinet SSL VPN Virtual Ethernet Adapter", "Fortinet SSL VPN")]
    [InlineData(System.Net.NetworkInformation.NetworkInterfaceType.Ppp, "Trabalho", "WAN Miniport (IKEv2)", "Trabalho")]
    [InlineData(System.Net.NetworkInformation.NetworkInterfaceType.Unknown, "wg0", "WireGuard Tunnel", "WireGuard Tunnel")]
    [InlineData(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "Ethernet 2", "TAP-Windows Adapter V9", "TAP-Windows Adapter V9")]
    [InlineData(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "Ethernet", "Intel(R) Ethernet Controller I225-V", null)]
    [InlineData(System.Net.NetworkInformation.NetworkInterfaceType.Tunnel, "Teredo", "Microsoft Teredo Tunneling Adapter", null)]
    [InlineData(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, "vEthernet", "Hyper-V Virtual Ethernet Adapter", null)]
    public void Vpn_adapters_are_recognized_by_type_or_name(System.Net.NetworkInformation.NetworkInterfaceType type, string name,
        string description, string? vpn)
    {
        Assert.Equal(vpn, WRApperture.Host.Core.Platform.WindowsNetworkProfiles.VpnName(type, name, description));
    }
}

public class DesktopShellTests
{
    [Theory]
    [InlineData(@"C:\WINDOWS\Explorer.EXE", true)] // started by Windows at sign-in
    [InlineData(@"""C:\Windows\explorer.exe""", true)]
    [InlineData(null, true)] // unknown: counts as the shell, as before
    [InlineData(@"C:\WINDOWS\explorer.exe /factory,{75dff2b7-6936-4c06-a8bb-676a7b00b24b} -Embedding", false)]
    [InlineData(@"""C:\Windows\explorer.exe"" ""C:\Program Files\Fortinet\FortiClient\FortiTray.exe""", false)]
    [InlineData(@"C:\Windows\explorer.exe C:\Users", false)]
    public void Only_a_bare_explorer_is_the_desktop_shell(string? commandLine, bool shell)
    {
        Assert.Equal(shell, DesktopShell.IsShell(commandLine));
    }
}
