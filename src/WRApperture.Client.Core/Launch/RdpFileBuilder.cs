using System.Text;
using WRApperture.Client.Core.Model;

namespace WRApperture.Client.Core.Launch;

/// <summary>Builds the .rdp contents that make mstsc open a single RemoteApp window (or the full desktop).</summary>
public static class RdpFileBuilder
{
    public static string FullAddress(PairedHost host) =>
        host.RdpPort == PairedHost.DefaultRdpPort ? host.Address.Trim() : $"{FormatHost(host.Address.Trim())}:{host.RdpPort}";

    public static string Build(PairedHost host, CachedApp app)
    {
        var sb = new StringBuilder();
        void Line(string key, string type, object value) => sb.Append(key).Append(':').Append(type).Append(':').Append(value).Append("\r\n");

        // Identical address and user for every app of a host => mstsc reuses one session (FR-009).
        Connection(host, Line);
        Line("remoteapplicationmode", "i", 1);
        Line("remoteapplicationprogram", "s", "||" + app.Alias);
        Line("remoteapplicationname", "s", SingleLine(app.Name));
        Line("remoteapplicationcmdline", "s", string.Empty);
        Line("disableremoteappcapscheck", "i", 1);
        Line("alternate shell", "s", "rdpinit.exe");
        Redirections(Line);
        return sb.ToString();
    }

    /// <summary>The host's whole desktop, full screen, resized with the client window.</summary>
    public static string BuildDesktop(PairedHost host)
    {
        var sb = new StringBuilder();
        void Line(string key, string type, object value) => sb.Append(key).Append(':').Append(type).Append(':').Append(value).Append("\r\n");

        Connection(host, Line);
        Line("remoteapplicationmode", "i", 0);
        Line("screen mode id", "i", 2);
        Line("use multimon", "i", 0);
        Line("dynamic resolution", "i", 1);
        Line("session bpp", "i", 32);
        Line("displayconnectionbar", "i", 1);
        Line("connection type", "i", 7);
        Line("networkautodetect", "i", 1);
        Line("bandwidthautodetect", "i", 1);
        Redirections(Line);
        return sb.ToString();
    }

    private static void Connection(PairedHost host, Action<string, string, object> line)
    {
        line("full address", "s", FullAddress(host));
        if (!string.IsNullOrWhiteSpace(host.RdpUserName))
        {
            line("username", "s", host.RdpUserName.Trim());
        }
    }

    private static void Redirections(Action<string, string, object> line)
    {
        line("prompt for credentials", "i", 0);
        line("authentication level", "i", 2);
        line("enablecredsspsupport", "i", 1);
        line("autoreconnection enabled", "i", 1);
        line("redirectclipboard", "i", 1);
        line("redirectprinters", "i", 0);
        line("drivestoredirect", "s", string.Empty);
        line("audiomode", "i", 0);
    }

    private static string FormatHost(string address) =>
        address.Contains(':') && !address.StartsWith('[') ? $"[{address}]" : address; // IPv6 literal

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
