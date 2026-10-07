using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Publishing;

namespace WRApperture.Host.Core.Agent;

internal sealed class AgentSettingsStore(
    IRegistryAccess registry,
    IPrivilegeChecker privileges,
    ILogger<AgentSettingsStore> logger) : IAgentSettingsStore
{
    private const string Path = RegistryPaths.AgentSettings;

    public AgentSettings Load()
    {
        var port = registry.GetValue(Path, "Port") is int p && IsValidPort(p) ? p : AgentSettings.DefaultPort;
        var paired = ReadCredentials() is not null;
        var thumbprint = registry.GetValue(Path, "CertificateThumbprint") as string;
        return new AgentSettings(port, paired, string.IsNullOrWhiteSpace(thumbprint) ? null : thumbprint);
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

        var credentials = ReadCredentials();
        return credentials is not null && TokenHasher.Verify(token, credentials.Value.Salt, credentials.Value.Hash);
    }

    public void SetPort(int port)
    {
        EnsureAdmin();
        if (!IsValidPort(port))
        {
            throw new AppValidationException("port", $"use uma porta entre {AgentSettings.MinPort} e {AgentSettings.MaxPort}.");
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
