using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Storage;

namespace WRApperture.Client.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wrapperture-tests", Guid.NewGuid().ToString("N"));

    public ClientPaths Paths => new(Path);

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class Samples
{
    public static PairedHost Host(string address = "192.168.0.10", int rdpPort = 3389, string? user = null) => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        DisplayName = "PC-ESCRITORIO",
        Address = address,
        AgentPort = 5080,
        ProtectedToken = HostStore.ProtectToken("secret-token"),
        PinnedFingerprint = "AB12",
        RdpPort = rdpPort,
        RdpUserName = user,
        Apps = [new CachedApp("bloco-de-notas", "Bloco de Notas"), new CachedApp("calc", "Calculadora")],
    };
}
