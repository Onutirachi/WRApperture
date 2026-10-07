namespace WRApperture.Host.Core.Agent;

public sealed record AgentSettings(int Port, bool IsPaired, string? CertificateThumbprint)
{
    public const int DefaultPort = 5080;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;
}

public interface IAgentSettingsStore
{
    /// <summary>Never requires admin (LocalSystem and administrators can read HKLM).</summary>
    AgentSettings Load();

    /// <summary>Creates a new pairing token, invalidating the previous one. Returns it in clear text exactly once. Requires admin.</summary>
    string RegenerateToken();

    /// <summary>Constant-time check. False when the host is not paired.</summary>
    bool VerifyToken(string? token);

    /// <summary>Requires admin.</summary>
    void SetPort(int port);

    /// <summary>Requires admin.</summary>
    void SetCertificateThumbprint(string thumbprint);

    /// <summary>Deletes all agent settings (token, port, certificate reference). Idempotent. Requires admin.</summary>
    void Reset();
}
