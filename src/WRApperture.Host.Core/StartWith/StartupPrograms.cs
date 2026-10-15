using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Discovery;

namespace WRApperture.Host.Core.StartWith;

/// <summary>A program Windows starts at sign-in (Run keys, Startup folder).</summary>
public sealed record StartupProgram(string Name, string ExecutablePath, string? Arguments);

public interface IStartupPrograms
{
    /// <summary>Enabled startup programs of the PC and, when given, of the user <paramref name="userSid"/>.</summary>
    IReadOnlyList<StartupProgram> List(string? userSid);

    /// <summary>
    /// Notification-area programs (".exe" with "tray" in the name) in <paramref name="folder"/>. Some vendors start
    /// them from a service instead of a startup entry: FortiClient's scheduler.exe starts FortiTray.exe.
    /// </summary>
    IReadOnlyList<StartupProgram> TrayProgramsIn(string folder);
}

/// <summary>
/// Reads what Explorer would start at sign-in: HKLM Run (64- and 32-bit), the user's HKCU Run and the all-users
/// Startup folder. Entries turned off in Task Manager (StartupApproved) are skipped.
/// </summary>
internal sealed class StartupPrograms(
    IRegistryAccess registry,
    IUserRegistryAccess users,
    IFileSystemProbe fs,
    IShortcutResolver shortcuts) : IStartupPrograms
{
    internal const string Run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    internal const string Run32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    internal const string ApprovedRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal const string ApprovedRun32 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    internal const string ApprovedFolder = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    /// <summary>"Tray" as a word of the name: FortiTray, lghub_system_tray, TRAY (not Strayer).</summary>
    private static readonly System.Text.RegularExpressions.Regex TrayWord =
        new(@"Tray|TRAY|(^|[^A-Za-z])tray", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The programs (.exe) of a folder, not its subfolders (replaceable in tests).</summary>
    internal Func<string, IEnumerable<string>> ExecutablesIn { get; init; } =
        folder => Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.exe") : [];

    /// <summary>Shortcut files of the all-users Startup folder (replaceable in tests).</summary>
    internal Func<IEnumerable<string>> StartupFolderShortcuts { get; init; } = DefaultStartupFolderShortcuts;

    public IReadOnlyList<StartupProgram> List(string? userSid)
    {
        var result = new List<StartupProgram>();
        AddRegistry(result, Run, ApprovedRun, read: registry.GetValueNames, get: registry.GetValue, approved: n => registry.GetValue(ApprovedRun, n));
        AddRegistry(result, Run32, ApprovedRun32, read: registry.GetValueNames, get: registry.GetValue, approved: n => registry.GetValue(ApprovedRun32, n));
        if (userSid is not null)
        {
            AddRegistry(result, Run, ApprovedRun,
                read: p => users.GetValueNames(userSid, p),
                get: (p, n) => users.GetValue(userSid, p, n),
                approved: n => users.GetValue(userSid, ApprovedRun, n));
        }

        foreach (var shortcut in StartupFolderShortcuts())
        {
            var name = Path.GetFileName(shortcut);
            if (IsDisabled(registry.GetValue(ApprovedFolder, name)) || shortcuts.Resolve(shortcut) is not { } target)
            {
                continue;
            }

            var exe = fs.Expand(target.TargetPath);
            if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new StartupProgram(Path.GetFileNameWithoutExtension(name), exe, NullIfEmpty(target.Arguments)));
            }
        }

        return result;
    }

    public IReadOnlyList<StartupProgram> TrayProgramsIn(string folder) =>
        ExecutablesIn(folder)
            .Where(path => TrayWord.IsMatch(Path.GetFileNameWithoutExtension(path)))
            .Select(path => new StartupProgram(Path.GetFileNameWithoutExtension(path), path, null))
            .ToList();

    private void AddRegistry(List<StartupProgram> result, string path, string approvedPath,
        Func<string, IReadOnlyList<string>> read, Func<string, string, object?> get, Func<string, object?> approved)
    {
        foreach (var name in read(path))
        {
            if (get(path, name) is not string commandLine || IsDisabled(approved(name)) ||
                CommandLine.Split(fs.Expand(commandLine)) is not { } parsed)
            {
                continue;
            }

            result.Add(new StartupProgram(name, parsed.Executable, parsed.Arguments));
        }
    }

    /// <summary>StartupApproved values: the first byte is even when enabled (02, 06) and odd when turned off (03, 07).</summary>
    private static bool IsDisabled(object? approval) => approval is byte[] { Length: > 0 } bytes && (bytes[0] & 1) == 1;

    private static IEnumerable<string> DefaultStartupFolderShortcuts()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.lnk") : [];
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Splits a command line such as Run values into the program (.exe) and its arguments.</summary>
internal static class CommandLine
{
    /// <summary>Null when it does not start with a program path ending in .exe.</summary>
    public static (string Executable, string? Arguments)? Split(string commandLine)
    {
        var text = commandLine.Trim();
        string exe;
        string rest;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end < 0)
            {
                return null;
            }

            exe = text[1..end];
            rest = text[(end + 1)..];
        }
        else
        {
            var end = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return null;
            }

            exe = text[..(end + 4)];
            rest = text[(end + 4)..];
        }

        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(exe))
        {
            return null;
        }

        rest = rest.Trim();
        return (exe, rest.Length == 0 ? null : rest);
    }
}
