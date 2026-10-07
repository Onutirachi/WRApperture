using Microsoft.Win32;

namespace WRApperture.Host.Core.Abstractions;

/// <summary>Access to per-user hives under HKEY_USERS. Paths are relative to HKU\&lt;SID&gt;.</summary>
public interface IUserRegistryAccess
{
    /// <summary>SIDs of local/domain user profiles whose hive is currently loaded (S-1-5-21-*).</summary>
    IReadOnlyList<string> LoadedUserSids();

    object? GetValue(string sid, string path, string name);
    void SetValue(string sid, string path, string name, object value, RegistryValueKind kind);
    void DeleteValue(string sid, string path, string name);
}
