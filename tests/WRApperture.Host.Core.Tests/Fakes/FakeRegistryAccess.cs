using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Tests.Fakes;

/// <summary>In-memory HKLM. Keys are created implicitly by SetValue, like RegistryKey.CreateSubKey.</summary>
internal sealed class FakeRegistryAccess : IRegistryAccess
{
    private readonly Dictionary<string, Dictionary<string, (object Value, RegistryValueKind Kind)>> _keys =
        new(StringComparer.OrdinalIgnoreCase);

    public int WriteCount { get; private set; }

    public bool KeyExists(string path) => _keys.ContainsKey(Norm(path));

    public IReadOnlyList<string> GetSubKeyNames(string path)
    {
        var prefix = Norm(path) + "\\";
        return _keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetValueNames(string path) =>
        _keys.TryGetValue(Norm(path), out var values) ? values.Keys.ToList() : [];

    public object? GetValue(string path, string name) =>
        _keys.TryGetValue(Norm(path), out var values) && values.TryGetValue(name, out var v) ? v.Value : null;

    public RegistryValueKind? GetKind(string path, string name) =>
        _keys.TryGetValue(Norm(path), out var values) && values.TryGetValue(name, out var v) ? v.Kind : null;

    public void SetValue(string path, string name, object value, RegistryValueKind kind)
    {
        WriteCount++;
        EnsureKey(Norm(path))[name] = (value, kind);
    }

    public void DeleteValue(string path, string name)
    {
        WriteCount++;
        if (_keys.TryGetValue(Norm(path), out var values))
        {
            values.Remove(name);
        }
    }

    public void DeleteKey(string path)
    {
        WriteCount++;
        var p = Norm(path);
        var doomed = _keys.Keys
            .Where(k => k.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                        k.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var k in doomed)
        {
            _keys.Remove(k);
        }
    }

    public void Seed(string path, string name, object value, RegistryValueKind kind = RegistryValueKind.String) =>
        EnsureKey(Norm(path))[name] = (value, kind);

    private Dictionary<string, (object Value, RegistryValueKind Kind)> EnsureKey(string path)
    {
        // Materialize parents so GetSubKeyNames/KeyExists behave like the real registry.
        var parts = path.Split('\\');
        for (var i = 1; i <= parts.Length; i++)
        {
            var p = string.Join('\\', parts[..i]);
            if (!_keys.ContainsKey(p))
            {
                _keys[p] = new(StringComparer.OrdinalIgnoreCase);
            }
        }

        return _keys[path];
    }

    private static string Norm(string path) => path.Trim('\\');
}
