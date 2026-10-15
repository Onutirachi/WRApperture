using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Core.Publishing;

internal sealed class AppPublisher(
    IRegistryAccess registry,
    IPrivilegeChecker privileges,
    IFileSystemProbe fs,
    ILogger<AppPublisher> logger) : IAppPublisher
{
    /// <summary>REG_MULTI_SZ: programs to start with the app (<see cref="PublishedApp.StartWith"/>).</summary>
    internal const string StartWithValue = "WRAppertureStartWith";

    /// <summary>DWORD 0: do not start the startup programs of the app's folder (absent = on).</summary>
    internal const string AutoStartWithValue = "WRAppertureAutoStartWith";

    public IReadOnlyList<PublishedApp> List() =>
        registry.GetSubKeyNames(RegistryPaths.Applications)
            .Select(Read)
            .OfType<PublishedApp>()
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public PublishedApp? Get(string alias) => AliasGenerator.IsValid(alias) ? Read(alias) : null;

    public PublishResult Publish(PublishRequest request)
    {
        EnsureAdmin();
        var validated = AppValidator.Validate(request, fs);
        var arguments = request.CommandLinePolicy == CommandLinePolicy.Required ? request.RequiredArguments!.Trim() : null;

        var existing = ResolveExisting(request, validated.ExecutablePath, arguments);
        var startWith = request.StartWith is null
            ? existing?.StartWithPrograms ?? []
            : ValidateStartWith(request.StartWith, validated.ExecutablePath);
        var alias = existing?.Alias ?? request.Alias ?? AliasGenerator.MakeUnique(
            AliasGenerator.Slugify(request.DisplayName),
            a => registry.KeyExists(RegistryPaths.App(a)));

        var desired = new PublishedApp(
            alias,
            request.DisplayName.Trim(),
            validated.ExecutablePath,
            validated.RawPath,
            validated.IconPath,
            request.IconIndex,
            request.CommandLinePolicy,
            arguments,
            request.ShowInList,
            IsManaged: existing?.IsManaged ?? true,
            startWith.Count == 0 ? null : startWith,
            request.AutoStartWith ?? existing?.AutoStartWith ?? true);

        if (existing is not null && Equivalent(existing, desired))
        {
            logger.LogInformation("RemoteApp {Alias} unchanged", alias);
            return new PublishResult(existing, PublishOutcome.Unchanged, validated.Warnings);
        }

        Write(desired);
        var outcome = existing is null ? PublishOutcome.Created : PublishOutcome.Updated;
        logger.LogInformation("RemoteApp {Alias} {Outcome}: {@Previous} -> {@Current}", alias, outcome, existing, desired);
        foreach (var warning in validated.Warnings)
        {
            logger.LogWarning("RemoteApp {Alias} warning {Warning} for {Path}", alias, warning, desired.ExecutablePath);
        }

        return new PublishResult(desired, outcome, validated.Warnings);
    }

    public bool Unpublish(string alias)
    {
        EnsureAdmin();
        if (!AliasGenerator.IsValid(alias))
        {
            // Guards against paths such as "..\x" deleting anything above the app's own key.
            throw new AppValidationException(nameof(alias), Strings.T("host.validation.aliasInvalid"));
        }

        var existing = Read(alias);
        if (existing is null)
        {
            logger.LogInformation("RemoteApp {Alias} not found; nothing to unpublish", alias);
            return false;
        }

        registry.DeleteKey(RegistryPaths.App(existing.Alias));
        logger.LogInformation("RemoteApp {Alias} unpublished: {@Previous}", existing.Alias, existing);
        return true;
    }

    /// <summary>Absolute, existing programs (.exe), without duplicates or the app itself.</summary>
    private IReadOnlyList<string> ValidateStartWith(IReadOnlyList<string> programs, string appPath)
    {
        var result = new List<string>();
        foreach (var program in programs.Select(p => fs.Expand(p.Trim().Trim('"'))).Where(p => p.Length > 0))
        {
            if (!Path.IsPathFullyQualified(program))
            {
                throw new AppValidationException("startWith", Strings.T("host.validation.pathNotAbsolute", program));
            }

            if (!program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new AppValidationException("startWith", Strings.T("host.validation.notExecutable"));
            }

            if (!fs.FileExists(program))
            {
                throw new AppValidationException("startWith", Strings.T("host.validation.fileNotFound", program));
            }

            if (!string.Equals(program, appPath, StringComparison.OrdinalIgnoreCase) &&
                !result.Contains(program, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(program);
            }
        }

        return result;
    }

    /// <summary>
    /// With an explicit alias, the target is that alias. Without one, an app with the same
    /// executable and arguments is reused so that publishing twice never duplicates it.
    /// </summary>
    private PublishedApp? ResolveExisting(PublishRequest request, string exe, string? arguments)
    {
        if (request.Alias is not null)
        {
            return Read(request.Alias);
        }

        return List().FirstOrDefault(a =>
            string.Equals(a.ExecutablePath, exe, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.RequiredArguments ?? string.Empty, arguments ?? string.Empty, StringComparison.Ordinal));
    }

    private PublishedApp? Read(string alias)
    {
        var key = RegistryPaths.App(alias);
        if (!registry.KeyExists(key))
        {
            return null;
        }

        // The registry is case-insensitive; report the alias with its stored casing.
        var storedAlias = registry.GetSubKeyNames(RegistryPaths.Applications)
            .FirstOrDefault(n => string.Equals(n, alias, StringComparison.OrdinalIgnoreCase)) ?? alias;

        var path = registry.GetValue(key, "Path") as string ?? string.Empty;
        var vpath = registry.GetValue(key, "VPath") as string;
        var iconPath = registry.GetValue(key, "IconPath") as string;
        var policy = AsInt(registry.GetValue(key, "CommandLineSetting"), 0);

        return new PublishedApp(
            storedAlias,
            registry.GetValue(key, "Name") as string ?? storedAlias,
            path,
            string.IsNullOrEmpty(vpath) || string.Equals(vpath, path, StringComparison.OrdinalIgnoreCase) ? null : vpath,
            string.IsNullOrEmpty(iconPath) ? path : iconPath,
            AsInt(registry.GetValue(key, "IconIndex"), 0),
            Enum.IsDefined(typeof(CommandLinePolicy), policy) ? (CommandLinePolicy)policy : CommandLinePolicy.None,
            NullIfEmpty(registry.GetValue(key, "RequiredCommandLine") as string),
            AsInt(registry.GetValue(key, "ShowInTSWA"), 1) != 0,
            AsInt(registry.GetValue(key, RegistryPaths.ManagedMarker), 0) == 1,
            registry.GetValue(key, StartWithValue) is string[] { Length: > 0 } startWith ? startWith : null,
            AsInt(registry.GetValue(key, AutoStartWithValue), 1) != 0);
    }

    private void Write(PublishedApp app)
    {
        var key = RegistryPaths.App(app.Alias);
        registry.SetValue(key, "Name", app.DisplayName, RegistryValueKind.String);
        registry.SetValue(key, "Path", app.ExecutablePath, RegistryValueKind.String);
        registry.SetValue(key, "VPath", app.RawPath ?? app.ExecutablePath, RegistryValueKind.String);
        registry.SetValue(key, "IconPath", app.IconPath, RegistryValueKind.String);
        registry.SetValue(key, "IconIndex", app.IconIndex, RegistryValueKind.DWord);
        registry.SetValue(key, "CommandLineSetting", (int)app.CommandLinePolicy, RegistryValueKind.DWord);
        registry.SetValue(key, "RequiredCommandLine", app.RequiredArguments ?? string.Empty, RegistryValueKind.String);
        registry.SetValue(key, "ShowInTSWA", app.ShowInList ? 1 : 0, RegistryValueKind.DWord);
        if (app.IsManaged)
        {
            registry.SetValue(key, RegistryPaths.ManagedMarker, 1, RegistryValueKind.DWord);
        }

        // Only written when set, so apps keep their keys exactly as before this feature otherwise.
        if (app.StartWithPrograms.Count > 0)
        {
            registry.SetValue(key, StartWithValue, app.StartWithPrograms.ToArray(), RegistryValueKind.MultiString);
        }
        else if (registry.GetValue(key, StartWithValue) is not null)
        {
            registry.DeleteValue(key, StartWithValue);
        }

        if (!app.AutoStartWith)
        {
            registry.SetValue(key, AutoStartWithValue, 0, RegistryValueKind.DWord);
        }
        else if (registry.GetValue(key, AutoStartWithValue) is not null)
        {
            registry.DeleteValue(key, AutoStartWithValue);
        }
    }

    private static bool Equivalent(PublishedApp a, PublishedApp b) =>
        a.DisplayName == b.DisplayName &&
        string.Equals(a.ExecutablePath, b.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.RawPath ?? string.Empty, b.RawPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.IconPath, b.IconPath, StringComparison.OrdinalIgnoreCase) &&
        a.IconIndex == b.IconIndex &&
        a.CommandLinePolicy == b.CommandLinePolicy &&
        (a.RequiredArguments ?? string.Empty) == (b.RequiredArguments ?? string.Empty) &&
        a.ShowInList == b.ShowInList &&
        a.StartWithPrograms.SequenceEqual(b.StartWithPrograms, StringComparer.OrdinalIgnoreCase) &&
        a.AutoStartWith == b.AutoStartWith;

    private void EnsureAdmin()
    {
        if (!privileges.IsAdministrator())
        {
            throw new AdminRequiredException();
        }
    }

    private static int AsInt(object? value, int fallback) => value is int i ? i : fallback;

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
