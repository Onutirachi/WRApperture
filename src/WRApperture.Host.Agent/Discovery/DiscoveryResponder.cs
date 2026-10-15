using System.Net;
using System.Net.Sockets;
using WRApperture.Host.Agent.Certificates;
using WRApperture.Host.Agent.Endpoints;
using WRApperture.Host.Core.Agent;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Discovery;

/// <summary>Answers clients looking for hosts on the local network (<see cref="Shared.Discovery"/>).</summary>
internal sealed class DiscoveryResponder(
    IAgentSettingsStore settings,
    AgentIdentity identity,
    IHostEnvironment environment,
    ILogger<DiscoveryResponder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing"))
        {
            return;
        }

        UdpClient udp;
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, ApiContract.DiscoveryPort));
        }
        catch (SocketException ex)
        {
            logger.LogWarning(ex, "Discovery disabled: UDP port {Port} unavailable", ApiContract.DiscoveryPort);
            return;
        }

        using (udp)
        {
            logger.LogInformation("Answering discovery on UDP {Port}", ApiContract.DiscoveryPort);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var received = await udp.ReceiveAsync(stoppingToken);
                    if (BuildReply(received.Buffer) is { } reply)
                    {
                        await udp.SendAsync(reply, received.RemoteEndPoint, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    // E.g. ICMP "port unreachable" from a client that already closed its socket.
                    logger.LogDebug(ex, "Discovery receive/send failed");
                }
            }
        }
    }

    /// <summary>The answer to a datagram, or null when it is not a discovery request (or the agent has no certificate yet).</summary>
    internal byte[]? BuildReply(byte[] datagram) =>
        Shared.Discovery.IsRequest(datagram) && identity.Fingerprint.Length > 0
            ? Shared.Discovery.EncodeReply(new DiscoveryReplyDto(Environment.MachineName, settings.Load().Port, identity.Fingerprint,
                AgentInfo.Version, AgentInfo.Features))
            : null;
}
