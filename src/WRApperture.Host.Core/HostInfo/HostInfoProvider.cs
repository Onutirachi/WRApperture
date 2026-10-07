using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Publishing;

namespace WRApperture.Host.Core.HostInfo;

public sealed record HostInfo(string HostName, int RdpPort);

public interface IHostInfoProvider
{
    HostInfo Get();
}

internal sealed class HostInfoProvider(IRegistryAccess registry) : IHostInfoProvider
{
    public const int DefaultRdpPort = 3389;

    public HostInfo Get()
    {
        var port = registry.GetValue(RegistryPaths.RdpTcp, "PortNumber") is int p and > 0 and <= 65535 ? p : DefaultRdpPort;
        return new HostInfo(Environment.MachineName, port);
    }
}
