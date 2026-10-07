using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Core.Publishing;

internal sealed record ValidatedRequest(
    string ExecutablePath,
    string? RawPath,
    string IconPath,
    IReadOnlyList<string> Warnings);

internal static class AppValidator
{
    public const int MaxDisplayName = 128;

    public static ValidatedRequest Validate(PublishRequest request, IFileSystemProbe fs)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new AppValidationException(nameof(request.DisplayName), Strings.T("host.validation.displayNameRequired"));
        }

        if (request.DisplayName.Trim().Length > MaxDisplayName)
        {
            throw new AppValidationException(nameof(request.DisplayName), Strings.T("host.validation.displayNameTooLong", MaxDisplayName));
        }

        if (request.Alias is not null && !AliasGenerator.IsValid(request.Alias))
        {
            throw new AppValidationException(nameof(request.Alias), Strings.T("host.validation.aliasCharacters"));
        }

        var exe = ExpandAndCheck(request.ExecutablePath, fs, nameof(request.ExecutablePath));
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppValidationException(nameof(request.ExecutablePath), Strings.T("host.validation.notExecutable"));
        }

        var icon = string.IsNullOrWhiteSpace(request.IconPath)
            ? exe
            : ExpandAndCheck(request.IconPath, fs, nameof(request.IconPath));

        if (request.IconIndex < 0)
        {
            throw new AppValidationException(nameof(request.IconIndex), Strings.T("host.validation.iconIndexNegative"));
        }

        if (request.CommandLinePolicy == CommandLinePolicy.Required && string.IsNullOrWhiteSpace(request.RequiredArguments))
        {
            throw new AppValidationException(nameof(request.RequiredArguments), Strings.T("host.validation.requiredArgumentsMissing"));
        }

        var raw = request.ExecutablePath.Trim();
        var warnings = new List<string>();
        if (fs.IsUserWritableLocation(exe))
        {
            warnings.Add(PublishWarnings.WritableLocation);
        }

        return new ValidatedRequest(exe, raw == exe ? null : raw, icon, warnings);
    }

    private static string ExpandAndCheck(string path, IFileSystemProbe fs, string field)
    {
        var expanded = fs.Expand((path ?? string.Empty).Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(expanded))
        {
            throw new AppValidationException(field, Strings.T("host.validation.pathNotAbsolute", path));
        }

        if (!fs.FileExists(expanded))
        {
            throw new AppValidationException(field, Strings.T("host.validation.fileNotFound", expanded));
        }

        return expanded;
    }
}
