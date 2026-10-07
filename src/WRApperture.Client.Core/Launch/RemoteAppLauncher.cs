using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Signing;
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
public sealed class RemoteAppLauncher(
    ClientPaths paths,
    IProcessStarter starter,
    IRdpSigner? signer = null,
    ILogger<RemoteAppLauncher>? logger = null)
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
        // Signed files from the trusted publisher open without the Windows security warning.
        // On failure the unsigned file still works (with the warning).
        signer?.TrySign(file);
        return file;
    }

    /// <summary>Signs .rdp files written before signing was configured (desktop shortcuts point to them).</summary>
    public int SignExistingFiles()
    {
        var root = Path.Combine(paths.Root, "rdp");
        if (signer is null || !Directory.Exists(root))
        {
            return 0;
        }

        return Directory.EnumerateFiles(root, "*.rdp", SearchOption.AllDirectories).Count(signer.TrySign);
    }

    public void Launch(PairedHost host, CachedApp app)
    {
        var file = WriteRdpFile(host, app);
        _logger.LogInformation("Launching RemoteApp {Alias} on {Address}", app.Alias, host.Address);
        starter.Start(MstscPath, $"\"{file}\"");
    }
}
