using System.Text.RegularExpressions;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Tests;

/// <summary>Keeps every language in localization/ consistent with English and with the code.</summary>
public partial class LocalizationTests
{
    public static TheoryData<string> OtherLanguages()
    {
        var data = new TheoryData<string>();
        foreach (var language in Strings.AvailableLanguages.Where(l => l != Strings.DefaultLanguage))
        {
            data.Add(language);
        }

        return data;
    }

    [Fact]
    public void English_and_portuguese_ship()
    {
        Assert.Contains("en", Strings.AvailableLanguages);
        Assert.Contains("pt", Strings.AvailableLanguages);
        Assert.NotEmpty(Strings.Entries("en"));
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Every_language_has_exactly_the_english_keys(string language)
    {
        var english = Strings.Entries(Strings.DefaultLanguage).Keys.ToHashSet();
        var translated = Strings.Entries(language).Keys.ToHashSet();

        Assert.Empty(english.Except(translated).Order()); // missing translations
        Assert.Empty(translated.Except(english).Order()); // keys English doesn't have (typos, leftovers)
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Placeholders_match_english(string language)
    {
        var english = Strings.Entries(Strings.DefaultLanguage);
        var mismatches = Strings.Entries(language)
            .Where(e => english.TryGetValue(e.Key, out var source) && Placeholders(source) != Placeholders(e.Value))
            .Select(e => e.Key)
            .ToList();

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Every_key_used_in_the_code_exists_in_english()
    {
        var english = Strings.Entries(Strings.DefaultLanguage);
        var root = RepoRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "packaging"), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml") || f.EndsWith(".ps1"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        var missing = files
            .SelectMany(f => KeyUse().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups["key"].Value)))
            .Where(u => !english.ContainsKey(u.Key))
            .Select(u => $"{u.File}: {u.Key}")
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Regional_language_falls_back_to_its_parent_then_english()
    {
        var portuguese = Strings.ChainFor("pt-BR");
        Assert.Equal(["pt-BR", "pt", "en"], portuguese);
        Assert.Equal("Tema escuro", Strings.Lookup("common.theme.dark", portuguese));

        var unknown = Strings.ChainFor("xx-YY");
        Assert.Equal("en", unknown[^1]);
        Assert.Equal("Dark theme", Strings.Lookup("common.theme.dark", unknown));
    }

    [Fact]
    public void Language_choices_list_every_shipped_language_by_its_own_name()
    {
        var choices = Strings.LanguageChoices();

        Assert.Equal(Strings.AvailableLanguages.Count, choices.Count);
        Assert.Contains(("en", "English"), choices);
        Assert.Contains(("pt", "Português"), choices);
    }

    [Fact]
    public void Unknown_key_returns_the_key_and_arguments_are_formatted()
    {
        Assert.Equal("nope.missing", Strings.T("nope.missing"));
        Assert.Equal("a 1 b", Strings.T("a {0} b", 1));
    }

    private static string Placeholders(string text) =>
        string.Join(",", Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order());

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WRApperture.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found");
    }

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    /// <summary>Strings.T("area.key"…), {loc:T area.key} in XAML, T 'area.key' in PowerShell.</summary>
    [GeneratedRegex("""(?:Strings\.T\(\s*"|\{loc:T\s+|(?<![\w-])T\s+'|L\(\s*")(?<key>[a-z][A-Za-z0-9]*\.[A-Za-z0-9.]+)""")]
    private static partial Regex KeyUse();
}
