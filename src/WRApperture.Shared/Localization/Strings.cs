using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace WRApperture.Shared.Localization;

/// <summary>
/// Product-wide translations. Each language is a folder under <c>localization/</c> (repo root) with one
/// JSON file per area (<c>client.json</c>, <c>manager.json</c>…); a key is "<c>area.key</c>". The files are
/// embedded in this assembly, so adding a language is just adding a folder and rebuilding.
/// <para>
/// The language follows the Windows display language: <c>pt-BR</c> looks in <c>pt-BR</c>, then <c>pt</c>,
/// then <see cref="DefaultLanguage"/>; a missing key falls back the same way and finally to the key itself.
/// It can be forced with the <c>WRAPPERTURE_LANG</c> environment variable or
/// <c>HKCU\Software\WRApperture\Ui\Language</c>.
/// </para>
/// </summary>
public static class Strings
{
    public const string DefaultLanguage = "en";
    public const string LanguageVariable = "WRAPPERTURE_LANG";
    private const string ResourcePrefix = "loc/";
    private const string SettingsKey = @"Software\WRApperture\Ui";
    private const string SettingsValue = "Language";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Catalog = new(LoadCatalog);
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static IReadOnlyList<string>? _chain;

    /// <summary>Languages shipped in this build (folder names), e.g. "en", "pt".</summary>
    public static IReadOnlyList<string> AvailableLanguages => Catalog.Value.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Lookup order for the current language, most specific first, always ending in <see cref="DefaultLanguage"/>.</summary>
    public static IReadOnlyList<string> Chain => _chain ??= ChainFor(RequestedLanguage() ?? CultureInfo.CurrentUICulture.Name);

    /// <summary>The language actually used (first of <see cref="Chain"/> that exists).</summary>
    public static string Language => Chain.FirstOrDefault(Catalog.Value.ContainsKey) ?? DefaultLanguage;

    /// <summary>
    /// The language chosen by the user in the apps (<c>HKCU\Software\WRApperture\Ui\Language</c>), or null for
    /// automatic (Windows display language). Takes effect on the next start of each app, wrahost and installers.
    /// </summary>
    public static string? PreferredLanguage
    {
        get => ReadPreference();
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
            if (string.IsNullOrWhiteSpace(value))
            {
                key.DeleteValue(SettingsValue, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(SettingsValue, value);
            }
        }
    }

    /// <summary>The Windows display language, e.g. "pt-BR" (what automatic mode follows).</summary>
    public static CultureInfo WindowsLanguage => CultureInfo.CurrentUICulture;

    /// <summary>Shipped languages with their own name ("English", "Português"), sorted by that name.</summary>
    public static IReadOnlyList<(string Code, string Name)> LanguageChoices() =>
        AvailableLanguages.Select(code => (code, NativeName(code))).OrderBy(l => l.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static string NativeName(string code)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(code).NativeName;
            return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.GetCultureInfo(code)) + name[1..] : code;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    /// <summary>Forces a language (tests, diagnostics). Null returns to the automatic choice.</summary>
    public static void Use(string? language)
    {
        _chain = language is null ? null : ChainFor(language);
        Cache.Clear();
    }

    public static string T(string key) => Cache.GetOrAdd(key, Resolve);

    /// <summary>Translation with <c>{0}</c>-style placeholders, formatted with the current culture.</summary>
    public static string T(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>All keys and texts of one language (tests and tooling).</summary>
    public static IReadOnlyDictionary<string, string> Entries(string language) =>
        Catalog.Value.TryGetValue(language, out var entries) ? entries : new Dictionary<string, string>();

    private static string Resolve(string key) => Lookup(key, Chain);

    /// <summary>The text for <paramref name="key"/> following <paramref name="chain"/>; the key itself when no language has it.</summary>
    public static string Lookup(string key, IReadOnlyList<string> chain)
    {
        foreach (var language in chain)
        {
            if (Catalog.Value.TryGetValue(language, out var entries) && entries.TryGetValue(key, out var text))
            {
                return text;
            }
        }

        return key;
    }

    /// <summary>"pt-BR" → ["pt-BR", "pt", "en"]; unknown names → ["en"].</summary>
    public static IReadOnlyList<string> ChainFor(string name)
    {
        var chain = new List<string>();
        try
        {
            for (var culture = CultureInfo.GetCultureInfo(name.Trim()); culture.Name.Length > 0; culture = culture.Parent)
            {
                chain.Add(culture.Name);
            }
        }
        catch (CultureNotFoundException)
        {
            // Unknown name: just the default language.
        }

        chain.Add(DefaultLanguage);
        return chain.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? RequestedLanguage()
    {
        var value = Environment.GetEnvironmentVariable(LanguageVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            value = ReadPreference();
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadPreference()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return key?.GetValue(SettingsValue) as string is { Length: > 0 } value ? value : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Embedded as "loc/&lt;language&gt;/&lt;area&gt;.json"; keys become "&lt;area&gt;.&lt;key&gt;".</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadCatalog()
    {
        var assembly = typeof(Strings).Assembly;
        var languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var path = name.Replace('\\', '/');
            if (!path.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = path[ResourcePrefix.Length..].Split('/');
            if (parts.Length != 2)
            {
                continue;
            }

            var area = Path.GetFileNameWithoutExtension(parts[1]);
            if (!languages.TryGetValue(parts[0], out var entries))
            {
                languages[parts[0]] = entries = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            foreach (var (key, text) in Read(assembly, name))
            {
                entries[$"{area}.{key}"] = text;
            }
        }

        return languages.ToDictionary(l => l.Key, l => (IReadOnlyDictionary<string, string>)l.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream,
                   new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
               ?? [];
    }
}
