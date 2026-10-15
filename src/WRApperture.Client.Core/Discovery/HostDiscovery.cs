using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WRApperture.Shared;

namespace WRApperture.Client.Core.Discovery;

/// <param name="Address">The IP address the answer came from.</param>
public sealed record DiscoveredHost(string HostName, string Address, int AgentPort, string Fingerprint, string Version,
    IReadOnlyList<string> Features)
{
    /// <summary>Agents before pairing by approval only pair with a token.</summary>
    public bool SupportsPairing => Features.Contains(AgentFeatures.Pairing, StringComparer.OrdinalIgnoreCase);
}

public interface IHostDiscovery
{
    /// <summary>Hosts answering on the local networks within <paramref name="duration"/>, by name.</summary>
    Task<IReadOnlyList<DiscoveredHost>> ScanAsync(TimeSpan duration, CancellationToken ct = default);

    /// <summary>The host's computer name when it resolves to the address that answered (stable across IP changes), else the IP.</summary>
    Task<string> ChooseAddressAsync(DiscoveredHost host, CancellationToken ct = default);
}

/// <summary>Finds agents by UDP broadcast (<see cref="Shared.Discovery"/>). Broadcasts do not cross routers or VPNs.</summary>
/// <param name="targets">Where to send the request; null: every local network's broadcast address.</param>
public sealed class HostDiscovery(
    int port = ApiContract.DiscoveryPort,
    Func<IReadOnlyList<IPAddress>>? targets = null,
    ILogger<HostDiscovery>? logger = null) : IHostDiscovery
{
    private static readonly TimeSpan ResendAfter = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan NameLookupTimeout = TimeSpan.FromSeconds(2);
    private readonly ILogger _logger = logger ?? NullLogger<HostDiscovery>.Instance;

    public async Task<IReadOnlyList<DiscoveredHost>> ScanAsync(TimeSpan duration, CancellationToken ct = default)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var found = new Dictionary<string, DiscoveredHost>(StringComparer.OrdinalIgnoreCase);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(duration);

        // Twice: a single datagram gets lost now and then.
        var sending = Task.Run(async () =>
        {
            await SendAsync(udp, deadline.Token);
            await Task.Delay(ResendAfter, deadline.Token);
            await SendAsync(udp, deadline.Token);
        }, deadline.Token);

        try
        {
            while (true)
            {
                var received = await udp.ReceiveAsync(deadline.Token);
                if (Shared.Discovery.DecodeReply(received.Buffer) is { } reply && !found.ContainsKey(reply.Fingerprint))
                {
                    found[reply.Fingerprint] = new DiscoveredHost(reply.HostName, received.RemoteEndPoint.Address.ToString(), reply.AgentPort,
                        reply.Fingerprint, reply.Version, reply.Features ?? []);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Scan time is up.
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "Discovery receive failed");
        }

        try
        {
            await sending;
        }
        catch (OperationCanceledException)
        {
        }

        ct.ThrowIfCancellationRequested();
        _logger.LogInformation("Discovery found {Count} host(s)", found.Count);
        return found.Values.OrderBy(h => h.HostName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<string> ChooseAddressAsync(DiscoveredHost host, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(NameLookupTimeout);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host.HostName, AddressFamily.InterNetwork, timeout.Token);
            if (addresses.Any(a => a.ToString() == host.Address))
            {
                return host.HostName;
            }
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Host name {Name} does not resolve; using {Address}", host.HostName, host.Address);
        }

        ct.ThrowIfCancellationRequested();
        return host.Address;
    }

    private async Task SendAsync(UdpClient udp, CancellationToken ct)
    {
        var request = Shared.Discovery.BuildRequest();
        foreach (var target in targets?.Invoke() ?? BroadcastAddresses())
        {
            try
            {
                await udp.SendAsync(request, new IPEndPoint(target, port), ct);
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Discovery send to {Target} failed", target);
            }
        }
    }

    /// <summary>255.255.255.255 plus each network's own broadcast address (some routers drop the global one).</summary>
    public static IReadOnlyList<IPAddress> BroadcastAddresses()
    {
        var result = new List<IPAddress> { IPAddress.Broadcast };
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            foreach (var unicast in network.GetIPProperties().UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                var address = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                if (mask.Length != 4)
                {
                    continue;
                }

                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++)
                {
                    broadcast[i] = (byte)(address[i] | ~mask[i]);
                }

                var target = new IPAddress(broadcast);
                if (!result.Contains(target))
                {
                    result.Add(target);
                }
            }
        }

        return result;
    }
}
