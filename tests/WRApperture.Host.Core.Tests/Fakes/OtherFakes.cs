using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Tests.Fakes;

internal sealed class FakeFirewallRules : IFirewallRules
{
    public Dictionary<string, bool> Rules { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RDP UserMode TCP"] = false,
        ["RDP UserMode UDP"] = false,
    };

    public IReadOnlyList<FirewallRule> GetRdpRules() => Rules.Select(r => new FirewallRule(r.Key, r.Value)).ToList();

    public void SetEnabled(string name, bool enabled) => Rules[name] = enabled;
}

internal sealed class FakePrivilegeChecker(bool isAdmin = true) : IPrivilegeChecker
{
    public bool IsAdmin { get; set; } = isAdmin;

    public bool IsAdministrator() => IsAdmin;
}

internal sealed class FakeFileSystemProbe : IFileSystemProbe
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> WritableDirs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Env { get; } = new(StringComparer.OrdinalIgnoreCase) { ["SystemRoot"] = @"C:\Windows" };

    public bool FileExists(string path) => Files.Contains(path);

    public string Expand(string path)
    {
        foreach (var (k, v) in Env)
        {
            path = path.Replace($"%{k}%", v, StringComparison.OrdinalIgnoreCase);
        }

        return path;
    }

    public bool IsUserWritableLocation(string filePath) =>
        WritableDirs.Contains(Path.GetDirectoryName(filePath) ?? string.Empty);
}
