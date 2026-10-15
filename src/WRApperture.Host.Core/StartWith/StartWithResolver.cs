using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Core.StartWith;

/// <param name="Automatic">Found among the startup programs of the app's folder (not chosen by hand).</param>
public sealed record StartWithProgram(string Name, string ExecutablePath, string? Arguments, bool Automatic);

/// <summary>What starts together with a published app in a remote app session.</summary>
public interface IStartWithResolver
{
    /// <summary>The app's chosen programs plus, unless turned off, the startup programs of its folder.</summary>
    IReadOnlyList<StartWithProgram> For(PublishedApp app, string? userSid);

    /// <summary>The startup programs that belong with the program at <paramref name="appPath"/> (its folder).</summary>
    IReadOnlyList<StartWithProgram> Detect(string appPath, string? userSid);
}

/// <summary>
/// A startup program belongs with an app when it lives in the app's folder, or one of the two folders contains the
/// other (e.g. a vendor's "bin" subfolder). Folders right below a drive and anything under Windows never match,
/// so publishing Notepad does not drag along whatever Windows starts from System32.
/// </summary>
internal sealed class StartWithResolver(IStartupPrograms startup) : IStartWithResolver
{
    private static readonly string WindowsFolder = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    public IReadOnlyList<StartWithProgram> For(PublishedApp app, string? userSid)
    {
        var chosen = app.StartWithPrograms
            .Select(p => new StartWithProgram(Path.GetFileNameWithoutExtension(p), p, null, Automatic: false))
            .ToList();
        var automatic = app.AutoStartWith ? Detect(app.ExecutablePath, userSid) : [];
        return chosen
            .Concat(automatic.Where(a => !chosen.Any(c => SamePath(c.ExecutablePath, a.ExecutablePath))))
            .ToList();
    }

    public IReadOnlyList<StartWithProgram> Detect(string appPath, string? userSid)
    {
        var appFolder = FolderOf(appPath);
        if (appFolder is null || IsUnder(appFolder, WindowsFolder))
        {
            return [];
        }

        return startup.List(userSid)
            .Where(p => !SamePath(p.ExecutablePath, appPath) && FolderOf(p.ExecutablePath) is { } folder && Related(appFolder, folder))
            .GroupBy(p => p.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(p => new StartWithProgram(p.Name, p.ExecutablePath, p.Arguments, Automatic: true))
            .ToList();
    }

    private static bool Related(string appFolder, string programFolder)
    {
        if (IsUnder(programFolder, WindowsFolder))
        {
            return false;
        }

        if (string.Equals(appFolder, programFolder, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var (outer, inner) = appFolder.Length < programFolder.Length ? (appFolder, programFolder) : (programFolder, appFolder);
        return IsUnder(inner, outer) && Depth(outer) >= 2;
    }

    private static string? FolderOf(string path) =>
        Path.GetDirectoryName(path) is { Length: > 0 } folder ? Normalize(folder) : null;

    private static string Normalize(string folder) => folder.TrimEnd('\\', '/');

    private static bool IsUnder(string folder, string root) =>
        root.Length > 0 && (string.Equals(folder, root, StringComparison.OrdinalIgnoreCase) ||
                            folder.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));

    /// <summary>"C:\Program Files" is 1, "C:\Program Files\Fortinet" is 2.</summary>
    private static int Depth(string folder) =>
        folder.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length - 1;

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
