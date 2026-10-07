using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Core.Discovery;

internal sealed record ShortcutFile(string Path, DiscoverySource Source);

internal sealed record ShortcutTarget(string TargetPath, string? Arguments, string? IconPath, int IconIndex);

/// <summary>Enumerates Start Menu shortcut files.</summary>
internal interface IStartMenuShortcuts
{
    IEnumerable<ShortcutFile> Enumerate();
}

/// <summary>Resolves a .lnk file to its target; null when it cannot be resolved.</summary>
internal interface IShortcutResolver
{
    ShortcutTarget? Resolve(string shortcutPath);
}

internal sealed class StartMenuShortcuts : IStartMenuShortcuts
{
    public IEnumerable<ShortcutFile> Enumerate()
    {
        var roots = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), DiscoverySource.AllUsers),
            (Environment.GetFolderPath(Environment.SpecialFolder.Programs), DiscoverySource.CurrentUser),
        };

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var (root, source) in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.lnk", options))
            {
                yield return new ShortcutFile(file, source);
            }
        }
    }
}
