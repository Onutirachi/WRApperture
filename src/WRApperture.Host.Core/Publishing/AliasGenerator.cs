using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WRApperture.Host.Core.Publishing;

/// <summary>Aliases are registry key names, URL segments and the "||alias" in .rdp files.</summary>
public static partial class AliasGenerator
{
    public const int MaxLength = 64;
    private const int SlugMaxLength = 60; // leaves room for a "-NN" suffix
    private const string Fallback = "app";

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ValidAlias();

    [GeneratedRegex("[^a-z0-9_-]+")]
    private static partial Regex InvalidChars();

    [GeneratedRegex("-{2,}")]
    private static partial Regex RepeatedDashes();

    public static bool IsValid(string? alias) => alias is not null && ValidAlias().IsMatch(alias);

    public static string Slugify(string name)
    {
        var decomposed = (name ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        var slug = InvalidChars().Replace(sb.ToString(), "-");
        slug = RepeatedDashes().Replace(slug, "-").Trim('-');
        if (slug.Length > SlugMaxLength)
        {
            slug = slug[..SlugMaxLength].TrimEnd('-');
        }

        return slug.Length == 0 ? Fallback : slug;
    }

    public static string MakeUnique(string baseAlias, Func<string, bool> isTaken)
    {
        if (!isTaken(baseAlias))
        {
            return baseAlias;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseAlias}-{i}";
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }
    }
}
