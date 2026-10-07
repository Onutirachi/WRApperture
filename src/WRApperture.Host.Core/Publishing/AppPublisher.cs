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
            IsManaged: existing?.IsManaged ?? true);

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
            AsInt(registry.GetValue(key, RegistryPaths.ManagedMarker), 0) == 1);
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
    }

    private static bool Equivalent(PublishedApp a, PublishedApp b) =>
        a.DisplayName == b.DisplayName &&
        string.Equals(a.ExecutablePath, b.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.RawPath ?? string.Empty, b.RawPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.IconPath, b.IconPath, StringComparison.OrdinalIgnoreCase) &&
        a.IconIndex == b.IconIndex &&
        a.CommandLinePolicy == b.CommandLinePolicy &&
        (a.RequiredArguments ?? string.Empty) == (b.RequiredArguments ?? string.Empty) &&
        a.ShowInList == b.ShowInList;

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
