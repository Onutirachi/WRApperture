using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Publishing;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Core.Agent;

internal sealed class AgentSettingsStore(
    IRegistryAccess registry,
    IPrivilegeChecker privileges,
    ILogger<AgentSettingsStore> logger) : IAgentSettingsStore
{
    private const string Path = RegistryPaths.AgentSettings;
    private const string AutoUpdateValue = "AutoUpdate";
    private const string ClientsPath = Path + @"\Clients";

    public AgentSettings Load()
    {
        var port = registry.GetValue(Path, "Port") is int p && IsValidPort(p) ? p : AgentSettings.DefaultPort;
        var paired = ReadCredentials() is not null || ClientKeys().Any();
        var thumbprint = registry.GetValue(Path, "CertificateThumbprint") as string;
        // Absent (or not a DWORD) means on: nobody watches a host, so it keeps itself up to date by default.
        var autoUpdate = registry.GetValue(Path, AutoUpdateValue) is not int flag || flag != 0;
        return new AgentSettings(port, paired, string.IsNullOrWhiteSpace(thumbprint) ? null : thumbprint, autoUpdate);
    }

    public string RegenerateToken()
    {
        EnsureAdmin();
        var token = TokenHasher.NewToken();
        var salt = TokenHasher.NewSalt();
        // Salt first: a reader seeing the new salt with the old hash fails closed (rejects), never open.
        registry.SetValue(Path, "TokenSalt", salt, RegistryValueKind.Binary);
        registry.SetValue(Path, "TokenHash", TokenHasher.Hash(token, salt), RegistryValueKind.Binary);
        logger.LogInformation("Agent pairing token regenerated");
        return token;
    }

    public bool VerifyToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        // Every candidate is hashed (no early exit), so timing does not tell which one matched.
        var match = false;
        foreach (var credentials in ClientKeys().Select(ReadClientCredentials).Prepend(ReadCredentials()))
        {
            if (credentials is { } c && TokenHasher.Verify(token, c.Salt, c.Hash))
            {
                match = true;
            }
        }

        return match;
    }

    public (PairedClient Client, string Token) AddClient(string name, string? user)
    {
        EnsureAdmin();
        var client = new PairedClient(Guid.NewGuid(), name, user, DateTimeOffset.UtcNow);
        var token = TokenHasher.NewToken();
        var salt = TokenHasher.NewSalt();
        var key = ClientKey(client.Id);
        registry.SetValue(key, "Name", client.Name, RegistryValueKind.String);
        if (!string.IsNullOrEmpty(user))
        {
            registry.SetValue(key, "User", user, RegistryValueKind.String);
        }

        registry.SetValue(key, "Created", client.CreatedUtc.ToString("O"), RegistryValueKind.String);
        registry.SetValue(key, "TokenSalt", salt, RegistryValueKind.Binary);
        registry.SetValue(key, "TokenHash", TokenHasher.Hash(token, salt), RegistryValueKind.Binary);
        logger.LogInformation("Client {Client} ({Name}) paired", client.Id, client.Name);
        return (client, token);
    }

    public IReadOnlyList<PairedClient> ListClients() =>
        ClientKeys()
            .Select(id => new PairedClient(id,
                registry.GetValue(ClientKey(id), "Name") as string ?? "?",
                registry.GetValue(ClientKey(id), "User") as string,
                DateTimeOffset.TryParse(registry.GetValue(ClientKey(id), "Created") as string, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var created) ? created : DateTimeOffset.MinValue))
            .OrderBy(c => c.CreatedUtc)
            .ToList();

    public bool RemoveClient(Guid id)
    {
        EnsureAdmin();
        var key = ClientKey(id);
        if (!registry.KeyExists(key))
        {
            return false;
        }

        registry.DeleteKey(key);
        logger.LogInformation("Client {Client} removed", id);
        return true;
    }

    private IEnumerable<Guid> ClientKeys() =>
        registry.KeyExists(ClientsPath)
            ? registry.GetSubKeyNames(ClientsPath).Select(n => Guid.TryParse(n, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)
            : [];

    private static string ClientKey(Guid id) => $@"{ClientsPath}\{id:D}";

    private (byte[] Salt, byte[] Hash)? ReadClientCredentials(Guid id) =>
        registry.GetValue(ClientKey(id), "TokenSalt") is byte[] { Length: TokenHasher.SaltBytes } salt &&
        registry.GetValue(ClientKey(id), "TokenHash") is byte[] { Length: 32 } hash
            ? (salt, hash)
            : null;

    public void SetPort(int port)
    {
        EnsureAdmin();
        if (!IsValidPort(port))
        {
            throw new AppValidationException("port", Strings.T("host.validation.port", AgentSettings.MinPort, AgentSettings.MaxPort));
        }

        registry.SetValue(Path, "Port", port, RegistryValueKind.DWord);
        logger.LogInformation("Agent port set to {Port}", port);
    }

    public void SetCertificateThumbprint(string thumbprint)
    {
        EnsureAdmin();
        registry.SetValue(Path, "CertificateThumbprint", thumbprint, RegistryValueKind.String);
        logger.LogInformation("Agent certificate set to {Thumbprint}", thumbprint);
    }

    public void SetAutoUpdate(bool enabled)
    {
        EnsureAdmin();
        registry.SetValue(Path, AutoUpdateValue, enabled ? 1 : 0, RegistryValueKind.DWord);
        logger.LogInformation("Agent automatic updates turned {State}", enabled ? "on" : "off");
    }

    public void Reset()
    {
        EnsureAdmin();
        if (registry.KeyExists(Path))
        {
            registry.DeleteKey(Path);
            logger.LogInformation("Agent settings reset");
        }
    }

    private (byte[] Salt, byte[] Hash)? ReadCredentials() =>
        registry.GetValue(Path, "TokenSalt") is byte[] { Length: TokenHasher.SaltBytes } salt &&
        registry.GetValue(Path, "TokenHash") is byte[] { Length: 32 } hash
            ? (salt, hash)
            : null;

    private static bool IsValidPort(int port) => port is >= AgentSettings.MinPort and <= AgentSettings.MaxPort;

    private void EnsureAdmin()
    {
        if (!privileges.IsAdministrator())
        {
            throw new AdminRequiredException();
        }
    }
}
