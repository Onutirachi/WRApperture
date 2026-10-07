using System.Text;
using WRApperture.Client.Core.Model;

namespace WRApperture.Client.Core.Launch;

/// <summary>Builds the .rdp contents that make mstsc open a single RemoteApp window.</summary>
public static class RdpFileBuilder
{
    public static string FullAddress(PairedHost host) =>
        host.RdpPort == PairedHost.DefaultRdpPort ? host.Address.Trim() : $"{FormatHost(host.Address.Trim())}:{host.RdpPort}";

    public static string Build(PairedHost host, CachedApp app)
    {
        var sb = new StringBuilder();
        void Line(string key, string type, object value) => sb.Append(key).Append(':').Append(type).Append(':').Append(value).Append("\r\n");

        // Identical address and user for every app of a host => mstsc reuses one session (FR-009).
        Line("full address", "s", FullAddress(host));
        if (!string.IsNullOrWhiteSpace(host.RdpUserName))
        {
            Line("username", "s", host.RdpUserName.Trim());
        }

        Line("remoteapplicationmode", "i", 1);
        Line("remoteapplicationprogram", "s", "||" + app.Alias);
        Line("remoteapplicationname", "s", SingleLine(app.DisplayName));
        Line("remoteapplicationcmdline", "s", string.Empty);
        Line("disableremoteappcapscheck", "i", 1);
        Line("alternate shell", "s", "rdpinit.exe");
        Line("prompt for credentials", "i", 0);
        Line("authentication level", "i", 2);
        Line("enablecredsspsupport", "i", 1);
        Line("autoreconnection enabled", "i", 1);
        Line("redirectclipboard", "i", 1);
        Line("redirectprinters", "i", 0);
        Line("drivestoredirect", "s", string.Empty);
        Line("audiomode", "i", 0);
        return sb.ToString();
    }

    private static string FormatHost(string address) =>
        address.Contains(':') && !address.StartsWith('[') ? $"[{address}]" : address; // IPv6 literal

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
