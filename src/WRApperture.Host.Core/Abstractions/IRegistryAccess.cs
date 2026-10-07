using Microsoft.Win32;

namespace WRApperture.Host.Core.Abstractions;

/// <summary>
/// Access to HKEY_LOCAL_MACHINE (64-bit view). Paths are relative to HKLM.
/// The only gateway to the registry allowed by the constitution (principle II).
/// </summary>
public interface IRegistryAccess
{
    bool KeyExists(string path);
    IReadOnlyList<string> GetSubKeyNames(string path);
    IReadOnlyList<string> GetValueNames(string path);
    object? GetValue(string path, string name);
    void SetValue(string path, string name, object value, RegistryValueKind kind);
    void DeleteValue(string path, string name);

    /// <summary>Deletes exactly <paramref name="path"/> and its own subtree; never a parent.</summary>
    void DeleteKey(string path);
}
