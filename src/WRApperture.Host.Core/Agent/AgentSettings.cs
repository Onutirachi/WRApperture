namespace WRApperture.Host.Core.Agent;

/// <param name="AutoUpdate">Install new releases unattended (HKLM\SOFTWARE\WRApperture\Agent\AutoUpdate, on when absent).</param>
public sealed record AgentSettings(int Port, bool IsPaired, string? CertificateThumbprint, bool AutoUpdate = true)
{
    public const int DefaultPort = 5080;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;
}

/// <summary>A client that paired by approval on the host's screen; it has its own token.</summary>
public sealed record PairedClient(Guid Id, string Name, string? User, DateTimeOffset CreatedUtc);

public interface IAgentSettingsStore
{
    /// <summary>Never requires admin (LocalSystem and administrators can read HKLM).</summary>
    AgentSettings Load();

    /// <summary>Creates a new pairing token, invalidating the previous one. Returns it in clear text exactly once. Requires admin.</summary>
    string RegenerateToken();

    /// <summary>Constant-time check against the pairing token and every client token. False when the host is not paired.</summary>
    bool VerifyToken(string? token);

    /// <summary>Registers a client with a token of its own, returned in clear text exactly once. Requires admin.</summary>
    (PairedClient Client, string Token) AddClient(string name, string? user);

    /// <summary>Clients with their own token, oldest first.</summary>
    IReadOnlyList<PairedClient> ListClients();

    /// <summary>Revokes a client's token. False when it does not exist. Requires admin.</summary>
    bool RemoveClient(Guid id);

    /// <summary>Requires admin.</summary>
    void SetPort(int port);

    /// <summary>Requires admin.</summary>
    void SetCertificateThumbprint(string thumbprint);

    /// <summary>Turns unattended updates on or off. Requires admin.</summary>
    void SetAutoUpdate(bool enabled);

    /// <summary>Deletes all agent settings (token, port, certificate reference). Idempotent. Requires admin.</summary>
    void Reset();
}
