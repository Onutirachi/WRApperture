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
    /// <summary>Starts the process; the caller owns (and disposes) the returned handle.</summary>
    Process? Start(string fileName, string arguments);
}

public sealed class ProcessStarter : IProcessStarter
{
    public Process? Start(string fileName, string arguments) =>
        Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false });
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

    /// <summary>"@" never appears in aliases ([A-Za-z0-9_-]), so this cannot collide with an app's file.</summary>
    public string DesktopRdpFilePath(PairedHost host) => Path.Combine(paths.HostRdp(host.Id), "@desktop.rdp");

    public string WriteRdpFile(PairedHost host, CachedApp app) => Write(RdpFilePath(host, app), RdpFileBuilder.Build(host, app));

    public string WriteDesktopRdpFile(PairedHost host) => Write(DesktopRdpFilePath(host), RdpFileBuilder.BuildDesktop(host));

    private string Write(string file, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // UTF-16 LE with BOM: the encoding mstsc itself writes.
        File.WriteAllText(file, contents, Encoding.Unicode);
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

    /// <summary>Returns the mstsc process (caller disposes) so the UI can wait for its window.</summary>
    public Process? Launch(PairedHost host, CachedApp app)
    {
        var file = WriteRdpFile(host, app);
        _logger.LogInformation("Launching RemoteApp {Alias} on {Address}", app.Alias, host.Address);
        return starter.Start(MstscPath, $"\"{file}\"");
    }

    public Process? LaunchDesktop(PairedHost host)
    {
        var file = WriteDesktopRdpFile(host);
        _logger.LogInformation("Launching full desktop on {Address}", host.Address);
        return starter.Start(MstscPath, $"\"{file}\"");
    }

    /// <summary>
    /// Completes when mstsc shows a window (its connection progress or the app) or exits (an existing
    /// RemoteApp session took over the request), or after <paramref name="timeout"/>.
    /// </summary>
    public static async Task WaitForRdpWindowAsync(Process? process, TimeSpan timeout, CancellationToken ct = default)
    {
        if (process is null)
        {
            return;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            process.Refresh();
            if (process.HasExited || process.MainWindowHandle != IntPtr.Zero)
            {
                return;
            }

            await Task.Delay(250, ct);
        }
    }
}
