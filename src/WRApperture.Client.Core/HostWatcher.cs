using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Model;

namespace WRApperture.Client.Core;

/// <summary>What the watcher noticed about a host.</summary>
public enum HostEvent
{
    /// <summary>Its state changed, or it answers again after being unreachable: sync it.</summary>
    Changed,

    /// <summary>It stopped answering.</summary>
    Unreachable,
}

/// <summary>
/// Follows one host through the agent's long poll (<see cref="IAgentApi.WaitForChangesAsync"/>) until cancelled. Only
/// for hosts that announce <see cref="Shared.AgentFeatures.Changes"/>. A refused token or a changed certificate is
/// reported as <see cref="HostEvent.Changed"/> and ends the watch: the sync that follows deals with it.
/// </summary>
public sealed class HostWatcher(Func<PairedHost, IAgentApi> clientFactory, ILogger<HostWatcher>? logger = null)
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger = logger ?? NullLogger<HostWatcher>.Instance;

    /// <param name="host">The host's current record (it may change while watching, e.g. a new address).</param>
    /// <param name="notify">Called on the caller's context; the watch waits for it before asking again.</param>
    public async Task WatchAsync(Func<PairedHost> host, Func<HostEvent, Task> notify, CancellationToken ct, TimeSpan? retryDelay = null)
    {
        string? version = null;
        var unreachable = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                string current;
                using (var client = clientFactory(host()))
                {
                    current = await client.WaitForChangesAsync(version, ct);
                }

                if ((version is not null && current != version) || unreachable)
                {
                    unreachable = false;
                    await notify(HostEvent.Changed);
                }

                version = current;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (AgentUnreachableException ex)
            {
                if (!unreachable)
                {
                    unreachable = true;
                    _logger.LogInformation(ex, "Host {Address} stopped answering", host().Address);
                    await notify(HostEvent.Unreachable);
                }

                if (!await DelayAsync(retryDelay, ct))
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is InvalidTokenException or CertificateChangedException)
            {
                await notify(HostEvent.Changed);
                return;
            }
            catch (AgentException ex)
            {
                // An error answer (not a network failure): ask again later.
                _logger.LogWarning(ex, "Watching host {Address} failed", host().Address);
                if (!await DelayAsync(retryDelay, ct))
                {
                    return;
                }
            }
        }
    }

    /// <summary>False when cancelled meanwhile.</summary>
    private static async Task<bool> DelayAsync(TimeSpan? delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay ?? RetryDelay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
