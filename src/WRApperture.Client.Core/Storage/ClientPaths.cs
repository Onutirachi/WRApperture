namespace WRApperture.Client.Core.Storage;

/// <summary>Layout under %LOCALAPPDATA%\WRApperture\client (or a custom root in tests).</summary>
public sealed class ClientPaths(string root)
{
    public static ClientPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WRApperture", "client"));

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WRApperture", "logs");

    public string Root { get; } = root;
    public string StateFile => Path.Combine(Root, "hosts.json");
    public string HostIcons(Guid hostId) => Path.Combine(Root, "icons", hostId.ToString("N"));
    public string HostRdp(Guid hostId) => Path.Combine(Root, "rdp", hostId.ToString("N"));

    /// <summary>Downloaded client setups (emptied before each download).</summary>
    public string Updates => Path.Combine(Root, "updates");

    /// <summary>Aliases are already safe ([A-Za-z0-9_-]); this guards against a malicious agent anyway.</summary>
    public static string SafeFileName(string alias)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(alias.Select(c => invalid.Contains(c) || c == '.' ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "app" : cleaned;
    }
}
