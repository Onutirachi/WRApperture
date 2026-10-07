using WRApperture.Host.Core.Publishing;

namespace WRApperture.Host.Core.Tests.Publishing;

public class AliasGeneratorTests
{
    [Theory]
    [InlineData("Bloco de Notas", "bloco-de-notas")]
    [InlineData("Calculadora Científica", "calculadora-cientifica")]
    [InlineData("  Excel   2021 ", "excel-2021")]
    [InlineData("C:\\Program Files\\App", "c-program-files-app")]
    [InlineData("Ação & Reação!!", "acao-reacao")]
    [InlineData("my_app-v2", "my_app-v2")]
    public void Slugify_produces_safe_alias(string name, string expected)
    {
        Assert.Equal(expected, AliasGenerator.Slugify(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("日本語")]
    public void Slugify_falls_back_when_nothing_is_left(string name)
    {
        Assert.Equal("app", AliasGenerator.Slugify(name));
    }

    [Fact]
    public void Slugify_truncates_long_names()
    {
        var alias = AliasGenerator.Slugify(new string('a', 200));
        Assert.True(alias.Length <= 60);
        Assert.True(AliasGenerator.IsValid(alias));
    }

    [Theory]
    [InlineData("notepad", true)]
    [InlineData("Note_Pad-2", true)]
    [InlineData("", false)]
    [InlineData("com espaço", false)]
    [InlineData("..\\Applications", false)]
    [InlineData("a/b", false)]
    [InlineData("ação", false)]
    public void IsValid_enforces_charset(string alias, bool expected)
    {
        Assert.Equal(expected, AliasGenerator.IsValid(alias));
    }

    [Fact]
    public void IsValid_rejects_more_than_64_chars()
    {
        Assert.True(AliasGenerator.IsValid(new string('a', 64)));
        Assert.False(AliasGenerator.IsValid(new string('a', 65)));
    }

    [Fact]
    public void MakeUnique_appends_numeric_suffix_case_insensitively()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Excel", "excel-2" };
        Assert.Equal("excel-3", AliasGenerator.MakeUnique("excel", taken.Contains));
        Assert.Equal("word", AliasGenerator.MakeUnique("word", taken.Contains));
    }
}
