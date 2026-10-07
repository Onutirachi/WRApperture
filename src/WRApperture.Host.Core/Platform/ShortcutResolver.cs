using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using WRApperture.Host.Core.Discovery;
using WRApperture.Host.Core.Platform.Native;

namespace WRApperture.Host.Core.Platform;

/// <summary>
/// Resolves regular shortcuts via IShellLinkW and MSI "advertised" shortcuts via msi.dll.
/// MsiGetShortcutTarget fails with 1603 on MTA threads (the .NET default), so work runs on an STA thread.
/// </summary>
internal sealed class ShortcutResolver(ILogger<ShortcutResolver> logger) : IShortcutResolver
{
    public ShortcutTarget? Resolve(string shortcutPath)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return ResolveCore(shortcutPath);
        }

        ShortcutTarget? result = null;
        var thread = new Thread(() => result = ResolveCore(shortcutPath)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private ShortcutTarget? ResolveCore(string shortcutPath)
    {
        try
        {
            var (path, args, iconPath, iconIndex) = ShellLinkNative.Read(shortcutPath);
            var target = ResolveAdvertised(shortcutPath) ?? path;
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            return new ShortcutTarget(
                Environment.ExpandEnvironmentVariables(target),
                string.IsNullOrWhiteSpace(args) ? null : args.Trim(),
                string.IsNullOrWhiteSpace(iconPath) ? null : Environment.ExpandEnvironmentVariables(iconPath),
                iconIndex);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or InvalidCastException)
        {
            logger.LogDebug(ex, "Could not resolve shortcut {Shortcut}", shortcutPath);
            return null;
        }
    }

    private static string? ResolveAdvertised(string shortcutPath)
    {
        var product = new StringBuilder(Msi.MaxGuidChars);
        var feature = new StringBuilder(Msi.MaxGuidChars + 1);
        var component = new StringBuilder(Msi.MaxGuidChars);
        if (Msi.MsiGetShortcutTargetW(shortcutPath, product, feature, component) != Msi.ErrorSuccess)
        {
            return null;
        }

        uint length = ShellLinkNative.MaxPath;
        var buffer = new StringBuilder((int)length);
        var state = Msi.MsiGetComponentPathW(product.ToString(), component.ToString(), buffer, ref length);
        // INSTALLSTATE_LOCAL (3) or INSTALLSTATE_SOURCE (4) mean a usable path.
        return state is 3 or 4 && buffer.Length > 0 ? buffer.ToString() : null;
    }
}
