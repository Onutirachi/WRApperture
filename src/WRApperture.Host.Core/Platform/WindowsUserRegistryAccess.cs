using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

internal sealed class WindowsUserRegistryAccess : IUserRegistryAccess
{
    private const string UserSidPrefix = "S-1-5-21-";

    public IReadOnlyList<string> LoadedUserSids() =>
        Registry.Users.GetSubKeyNames()
            .Where(n => n.StartsWith(UserSidPrefix, StringComparison.OrdinalIgnoreCase) &&
                        !n.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public object? GetValue(string sid, string path, string name)
    {
        using var key = Registry.Users.OpenSubKey($@"{sid}\{path}");
        return key?.GetValue(name);
    }

    public void SetValue(string sid, string path, string name, object value, RegistryValueKind kind)
    {
        using var key = Registry.Users.CreateSubKey($@"{sid}\{path}", writable: true);
        key.SetValue(name, value, kind);
    }

    public void DeleteValue(string sid, string path, string name)
    {
        using var key = Registry.Users.OpenSubKey($@"{sid}\{path}", writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
