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

internal sealed class FakeUserRegistryAccess : IUserRegistryAccess
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Loaded { get; } = [];

    public IReadOnlyList<string> LoadedUserSids() => Loaded;

    public object? GetValue(string sid, string path, string name) => _values.GetValueOrDefault(Key(sid, path, name));

    public IReadOnlyList<string> GetValueNames(string sid, string path) =>
        _values.Keys.Where(k => k.StartsWith($@"{sid}\{path}\", StringComparison.OrdinalIgnoreCase) &&
                                !k[($@"{sid}\{path}\").Length..].Contains('\\'))
            .Select(k => k[($@"{sid}\{path}\").Length..]).ToList();

    public void SetValue(string sid, string path, string name, object value, Microsoft.Win32.RegistryValueKind kind) =>
        _values[Key(sid, path, name)] = value;

    public void DeleteValue(string sid, string path, string name) => _values.Remove(Key(sid, path, name));

    private static string Key(string sid, string path, string name) => $@"{sid}\{path}\{name}";
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

internal sealed class FakeNetworkProfiles : INetworkProfiles
{
    public static readonly Guid Home = new("11111111-1111-1111-1111-111111111111");

    public List<NetworkProfile> Networks { get; } = [new(Home, "Casa", NetworkCategory.Private)];

    public IReadOnlyList<NetworkProfile> Connected() => Networks.ToList();

    public void SetCategory(Guid networkId, NetworkCategory category)
    {
        var i = Networks.FindIndex(n => n.Id == networkId);
        Networks[i] = Networks[i] with { Category = category };
    }
}

internal sealed class FakeLocalAccounts : ILocalAccounts
{
    public const string Sid = "S-1-5-21-1-2-3-1001";

    /// <summary>Defaults to a local administrator with a password.</summary>
    public LocalAccount? Console { get; set; } = new("carlos", "PC-TESTE", Sid, true, null, true, false);

    public PasswordStatus Password { get; set; } = PasswordStatus.Set;
    public string? NewPassword { get; private set; }
    public HashSet<string> RemoteDesktopUsers { get; } = [];

    public LocalAccount? ConsoleUser() =>
        Console is null ? null : Console with { IsRemoteDesktopUser = Console.IsRemoteDesktopUser || RemoteDesktopUsers.Contains(Console.Sid) };

    public PasswordStatus GetPasswordStatus(LocalAccount account) => Password;

    public void SetPassword(LocalAccount account, string password)
    {
        NewPassword = password;
        Password = PasswordStatus.Set;
    }

    public void AddToRemoteDesktopUsers(string sid) => RemoteDesktopUsers.Add(sid);

    public void RemoveFromRemoteDesktopUsers(string sid) => RemoteDesktopUsers.Remove(sid);
}

internal sealed class FakeServiceControl : IServiceControl
{
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning(string name) => Running.Contains(name);

    public void Start(string name) => Running.Add(name);
}

internal sealed class FakePowerSettings : IPowerSettings
{
    public TimeSpan? Sleep { get; set; }

    public TimeSpan? SleepAfter() => Sleep;
}
