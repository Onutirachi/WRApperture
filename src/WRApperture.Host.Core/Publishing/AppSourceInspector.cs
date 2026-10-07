using System.Diagnostics;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Discovery;
using WRApperture.Host.Core.Errors;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Core.Publishing;

/// <summary>What a dropped shortcut or executable resolves to, ready to be reviewed and published.</summary>
public sealed record AppDraft(string DisplayName, string ExecutablePath, string? Arguments, string IconPath, int IconIndex);

public interface IAppSourceInspector
{
    /// <summary>Reads a .lnk (including MSI advertised shortcuts) or .exe. Throws <see cref="AppValidationException"/> with a user-facing reason.</summary>
    AppDraft Inspect(string path);
}

/// <summary>Human-friendly program name from the executable's version resource.</summary>
public interface IFileDescriptionReader
{
    string? Describe(string executablePath);
}

internal sealed class FileDescriptionReader : IFileDescriptionReader
{
    public string? Describe(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}

internal sealed class AppSourceInspector(IShortcutResolver resolver, IFileSystemProbe fs, IFileDescriptionReader descriptions)
    : IAppSourceInspector
{
    private const string Field = "source";

    public AppDraft Inspect(string path)
    {
        var full = fs.Expand((path ?? string.Empty).Trim().Trim('"'));
        if (!fs.FileExists(full))
        {
            throw new AppValidationException(Field, Strings.T("host.inspect.fileNotFound", full));
        }

        return Path.GetExtension(full).ToLowerInvariant() switch
        {
            ".lnk" => FromShortcut(full),
            ".exe" => FromExecutable(full),
            _ => throw NotAProgram(full),
        };
    }

    private AppDraft FromShortcut(string lnk)
    {
        var target = resolver.Resolve(lnk)
                     ?? throw new AppValidationException(Field, Strings.T("host.inspect.shortcutUnreadable", Path.GetFileName(lnk)));

        if (target.TargetPath.StartsWith(fs.Expand(@"%SystemRoot%\Installer\"), StringComparison.OrdinalIgnoreCase))
        {
            throw new AppValidationException(Field,
                Strings.T("host.inspect.shortcutToInstaller", Path.GetFileName(lnk)));
        }

        if (!target.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw NotAProgram(target.TargetPath);
        }

        if (!fs.FileExists(target.TargetPath))
        {
            throw new AppValidationException(Field, Strings.T("host.inspect.shortcutTargetMissing", target.TargetPath));
        }

        var iconOk = !string.IsNullOrEmpty(target.IconPath) && fs.FileExists(target.IconPath);
        return new AppDraft(
            Limit(Path.GetFileNameWithoutExtension(lnk)),
            target.TargetPath,
            string.IsNullOrWhiteSpace(target.Arguments) ? null : target.Arguments.Trim(),
            iconOk ? target.IconPath! : target.TargetPath,
            iconOk ? target.IconIndex : 0);
    }

    private AppDraft FromExecutable(string exe)
    {
        var name = descriptions.Describe(exe)?.Trim();
        return new AppDraft(Limit(string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(exe) : name), exe, null, exe, 0);
    }

    private static AppValidationException NotAProgram(string path) =>
        new(Field, Strings.T("host.inspect.notAProgram", Path.GetFileName(path)));

    private static string Limit(string name) =>
        name.Length <= AppValidator.MaxDisplayName ? name : name[..AppValidator.MaxDisplayName];
}
