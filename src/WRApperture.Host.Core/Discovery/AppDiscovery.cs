using Microsoft.Extensions.Logging;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Core.Discovery;

internal sealed class AppDiscovery(
    IStartMenuShortcuts shortcuts,
    IShortcutResolver resolver,
    IFileSystemProbe fs,
    IAppPublisher publisher,
    ILogger<AppDiscovery> logger) : IAppDiscovery
{
    private static readonly string[] ExcludedFilePrefixes = ["unins", "uninstall", "setup"];
    private static readonly string[] ExcludedNameFragments = ["uninstall", "desinstalar", "remover "];

    public IReadOnlyList<DiscoveredApp> Discover()
    {
        var published = publisher.List();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DiscoveredApp>();
        var skipped = 0;

        // AllUsers shortcuts come first, so they win over per-user duplicates.
        foreach (var file in shortcuts.Enumerate().OrderBy(f => f.Source))
        {
            var displayName = Path.GetFileNameWithoutExtension(file.Path);
            var target = resolver.Resolve(file.Path);
            if (target is null || !IsApplication(displayName, target.TargetPath))
            {
                skipped++;
                continue;
            }

            if (!seen.Add($"{target.TargetPath}\n{target.Arguments}"))
            {
                continue;
            }

            var iconPath = !string.IsNullOrEmpty(target.IconPath) && fs.FileExists(target.IconPath)
                ? target.IconPath
                : target.TargetPath;
            var iconIndex = iconPath == target.IconPath ? target.IconIndex : 0;

            var alias = published.FirstOrDefault(p =>
                string.Equals(p.ExecutablePath, target.TargetPath, StringComparison.OrdinalIgnoreCase) &&
                (p.RequiredArguments ?? string.Empty) == (target.Arguments ?? string.Empty))?.Alias;

            result.Add(new DiscoveredApp(displayName, target.TargetPath, target.Arguments, iconPath, iconIndex, file.Source, alias));
        }

        logger.LogInformation("Discovery found {Count} apps ({Skipped} shortcuts skipped)", result.Count, skipped);
        return result.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private bool IsApplication(string displayName, string targetPath)
    {
        if (!targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !fs.FileExists(targetPath))
        {
            return false;
        }

        // Unresolved MSI "advertised" shortcuts point at icon stubs in %SystemRoot%\Installer.
        if (targetPath.StartsWith(fs.Expand(@"%SystemRoot%\Installer\"), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(targetPath);
        if (ExcludedFilePrefixes.Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !ExcludedNameFragments.Any(f => displayName.Contains(f, StringComparison.OrdinalIgnoreCase));
    }
}
