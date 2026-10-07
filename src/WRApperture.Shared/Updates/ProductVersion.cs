using System.Reflection;

namespace WRApperture.Shared.Updates;

/// <summary>Product versions are "X.Y.Z" (tag "vX.Y.Z"), always compared with exactly three parts.</summary>
public static class ProductVersion
{
    private static readonly Lazy<Version> CurrentVersion = new(() => FromAssembly(typeof(ProductVersion).Assembly));

    /// <summary>
    /// This build's version. Every project gets the same <c>Version</c> from Directory.Build.props, so this
    /// assembly's InformationalVersion is the executable's (and stays right under a test host).
    /// </summary>
    public static Version Current => CurrentVersion.Value;

    public static Version FromAssembly(Assembly assembly) =>
        Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, allowPreRelease: true)
        ?? Normalize(assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// "v1.2.3", "1.2.3" or "1.2.3+commit" → 1.2.3. Pre-release versions ("1.3.0-beta.1") are ignored (null)
    /// unless <paramref name="allowPreRelease"/>, which keeps just the "1.3.0" part (a local build's own version).
    /// </summary>
    public static Version? Parse(string? text, bool allowPreRelease = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        value = value.Split('+')[0];
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            if (!allowPreRelease)
            {
                return null;
            }

            value = value[..dash];
        }

        return Version.TryParse(value, out var version) && version.Major >= 0 ? Normalize(version) : null;
    }

    public static string Format(Version version) => Normalize(version).ToString(3);

    /// <summary>
    /// True when <paramref name="candidate"/> (e.g. an agent's "0.14.1") is older than <paramref name="latest"/>;
    /// false when it cannot be parsed (unknown is never reported as outdated).
    /// </summary>
    public static bool IsOlder(string? candidate, Version latest) => Parse(candidate, allowPreRelease: true) is { } version && version < latest;

    /// <summary>System.Version treats 1.2.3 and 1.2.3.0 as different: keep exactly Major.Minor.Build.</summary>
    private static Version Normalize(Version version) => new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
}
