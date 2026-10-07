using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Storage;

namespace WRApperture.Client.Core.Launch;

public interface IProcessStarter
{
    void Start(string fileName, string arguments);
}

public sealed class ProcessStarter : IProcessStarter
{
    public void Start(string fileName, string arguments) =>
        Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false })?.Dispose();
}

/// <summary>Writes the per-app .rdp file and hands it to the Windows Remote Desktop client.</summary>
public sealed class RemoteAppLauncher(ClientPaths paths, IProcessStarter starter, ILogger<RemoteAppLauncher>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<RemoteAppLauncher>.Instance;

    public static string MstscPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");

    public string RdpFilePath(PairedHost host, CachedApp app) =>
        Path.Combine(paths.HostRdp(host.Id), ClientPaths.SafeFileName(app.Alias) + ".rdp");

    public string WriteRdpFile(PairedHost host, CachedApp app)
    {
        var file = RdpFilePath(host, app);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // UTF-16 LE with BOM: the encoding mstsc itself writes.
        File.WriteAllText(file, RdpFileBuilder.Build(host, app), Encoding.Unicode);
        return file;
    }

    public void Launch(PairedHost host, CachedApp app)
    {
        var file = WriteRdpFile(host, app);
        _logger.LogInformation("Launching RemoteApp {Alias} on {Address}", app.Alias, host.Address);
        starter.Start(MstscPath, $"\"{file}\"");
    }
}
