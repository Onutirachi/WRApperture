# Contract: WRApperture.Client.Core

Consome a API HTTP do agente definida em [../../20261007-105635-host-agent/contracts/http-api.md](../../20261007-105635-host-agent/contracts/http-api.md).

```csharp
public sealed class AgentClient : IDisposable
{
    public static Task<PairingResult> PairAsync(string address, int agentPort, string token, CancellationToken ct = default);
    public AgentClient(PairedHost host, string token);                // pinned
    public Task<HostInfoDto> GetHostInfoAsync(CancellationToken ct = default);
    public Task<IReadOnlyList<AppSummaryDto>> GetAppsAsync(CancellationToken ct = default);
    public Task<IconResult> GetIconAsync(string alias, int size, string? etag, CancellationToken ct = default);
}
public sealed record PairingResult(HostInfoDto Host, string ObservedFingerprint);
public sealed record IconResult(bool NotModified, byte[]? Png, string? ETag);

public sealed class HostStore            // hosts.json + DPAPI
{
    public HostStore(string rootDirectory);
    public ClientState Load();
    public void Save(ClientState state);
    public static string ProtectToken(string token);
    public static string UnprotectToken(string protectedToken);
}

public static class RdpFileBuilder { public static string Build(PairedHost host, CachedApp app); }

public sealed class RemoteAppLauncher   // escreve o .rdp e chama mstsc
{
    public RemoteAppLauncher(string rootDirectory, IProcessStarter starter);
    public string WriteRdpFile(PairedHost host, CachedApp app);
    public void Launch(PairedHost host, CachedApp app);
}

public interface ICredentialStore
{
    void Save(string address, string userName, string password);
    bool Exists(string address);
    void Delete(string address);
}
```
