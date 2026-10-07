using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

internal sealed class WindowsRegistryAccess : IRegistryAccess
{
    private static RegistryKey Hklm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    public bool KeyExists(string path)
    {
        using var root = Hklm();
        using var key = root.OpenSubKey(path);
        return key is not null;
    }

    public IReadOnlyList<string> GetSubKeyNames(string path)
    {
        using var root = Hklm();
        using var key = root.OpenSubKey(path);
        return key?.GetSubKeyNames() ?? [];
    }

    public IReadOnlyList<string> GetValueNames(string path)
    {
        using var root = Hklm();
        using var key = root.OpenSubKey(path);
        return key?.GetValueNames() ?? [];
    }

    public object? GetValue(string path, string name)
    {
        using var root = Hklm();
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public void SetValue(string path, string name, object value, RegistryValueKind kind)
    {
        using var root = Hklm();
        using var key = root.CreateSubKey(path, writable: true);
        key.SetValue(name, value, kind);
    }

    public void DeleteValue(string path, string name)
    {
        using var root = Hklm();
        using var key = root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKey(string path)
    {
        using var root = Hklm();
        root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }
}
