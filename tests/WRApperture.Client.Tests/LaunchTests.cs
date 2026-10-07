using System.Text;
using WRApperture.Client.Core.Launch;
using WRApperture.Client.Core.Model;

namespace WRApperture.Client.Tests;

public class RdpFileBuilderTests
{
    private static Dictionary<string, string> Parse(string rdp) =>
        rdp.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(':', 3))
            .ToDictionary(p => p[0], p => p[2]);

    [Fact]
    public void Contains_remoteapp_essentials()
    {
        var rdp = Parse(RdpFileBuilder.Build(Samples.Host(), new CachedApp("bloco-de-notas", "Bloco de Notas")));

        Assert.Equal("192.168.0.10", rdp["full address"]);
        Assert.Equal("1", rdp["remoteapplicationmode"]);
        Assert.Equal("||bloco-de-notas", rdp["remoteapplicationprogram"]);
        Assert.Equal("Bloco de Notas", rdp["remoteapplicationname"]);
        Assert.Equal("1", rdp["disableremoteappcapscheck"]);
        Assert.Equal("rdpinit.exe", rdp["alternate shell"]);
        Assert.False(rdp.ContainsKey("username"));
    }

    [Fact]
    public void Uses_non_default_rdp_port_and_username()
    {
        var rdp = Parse(RdpFileBuilder.Build(Samples.Host(rdpPort: 3390, user: "PC\\joao"), new CachedApp("calc", "Calc")));

        Assert.Equal("192.168.0.10:3390", rdp["full address"]);
        Assert.Equal("PC\\joao", rdp["username"]);
    }

    [Fact]
    public void Brackets_ipv6_when_port_is_appended()
    {
        Assert.Equal("[fe80::1]:3390", RdpFileBuilder.FullAddress(Samples.Host("fe80::1", rdpPort: 3390)));
        Assert.Equal("fe80::1", RdpFileBuilder.FullAddress(Samples.Host("fe80::1")));
    }

    [Fact]
    public void All_apps_of_a_host_share_address_and_user_for_session_reuse()
    {
        var host = Samples.Host(user: "joao");
        var a = Parse(RdpFileBuilder.Build(host, host.Apps[0]));
        var b = Parse(RdpFileBuilder.Build(host, host.Apps[1]));

        Assert.Equal(a["full address"], b["full address"]);
        Assert.Equal(a["username"], b["username"]);
    }

    [Fact]
    public void Special_characters_in_names_stay_on_one_line()
    {
        var rdp = RdpFileBuilder.Build(Samples.Host(), new CachedApp("x", "Ação & Reação\r\nfull address:s:evil"));

        Assert.Contains("remoteapplicationname:s:Ação & Reação  full address:s:evil\r\n", rdp);
        Assert.Equal("192.168.0.10", Parse(rdp)["full address"]);
    }
}

public class RemoteAppLauncherTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Writes_utf16_file_and_starts_mstsc_with_quoted_path()
    {
        var starter = new RecordingStarter();
        var launcher = new RemoteAppLauncher(_dir.Paths, starter);
        var host = Samples.Host();

        launcher.Launch(host, host.Apps[0]);

        var file = launcher.RdpFilePath(host, host.Apps[0]);
        var bytes = File.ReadAllBytes(file);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, bytes[..2]);
        Assert.Contains("||bloco-de-notas", Encoding.Unicode.GetString(bytes));
        Assert.EndsWith("mstsc.exe", starter.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"\"{file}\"", starter.Arguments);
    }

    [Fact]
    public void Files_of_different_hosts_do_not_collide()
    {
        var launcher = new RemoteAppLauncher(_dir.Paths, new RecordingStarter());
        var h1 = Samples.Host();
        var h2 = h1 with { Id = Guid.NewGuid() };

        Assert.NotEqual(launcher.WriteRdpFile(h1, h1.Apps[0]), launcher.WriteRdpFile(h2, h2.Apps[0]));
    }

    private sealed class RecordingStarter : IProcessStarter
    {
        public string FileName { get; private set; } = string.Empty;
        public string Arguments { get; private set; } = string.Empty;

        public System.Diagnostics.Process? Start(string fileName, string arguments)
        {
            FileName = fileName;
            Arguments = arguments;
            return null;
        }
    }
}
