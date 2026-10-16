using System.IO;
using Microsoft.Extensions.Logging;
using WRApperture.Client.Core;
using WRApperture.Client.Core.Agent;
using WRApperture.Shared;

namespace WRApperture.Client.ViewModels;

/// <summary>
/// Live changes: every host whose agent announces <see cref="AgentFeatures.Changes"/> is followed (one waiting request
/// each) and synced as soon as it changes, selected or not. Its card turns Offline when it stops answering.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>How long a change for the selected host waits for a sync or an app launch in progress.</summary>
    private static readonly TimeSpan BusyWait = TimeSpan.FromSeconds(30);

    private readonly Dictionary<Guid, CancellationTokenSource> _watches = [];
    private bool _watching;

    private void StartWatching()
    {
        _watching = true;
        UpdateWatches();
    }

    /// <summary>One watch per host that supports it; removed hosts stop being watched.</summary>
    private void UpdateWatches()
    {
        if (!_watching)
        {
            return;
        }

        foreach (var (id, watch) in _watches.ToList())
        {
            if (!Hosts.Any(h => h.Host.Id == id && h.Host.Supports(AgentFeatures.Changes)))
            {
                _watches.Remove(id);
                watch.Cancel();
            }
        }

        foreach (var option in Hosts.Where(h => h.Host.Supports(AgentFeatures.Changes) && !_watches.ContainsKey(h.Host.Id)).ToList())
        {
            var watch = new CancellationTokenSource();
            _watches[option.Host.Id] = watch;
            _ = WatchAsync(option, watch);
        }
    }

    private async Task WatchAsync(HostOption option, CancellationTokenSource watch)
    {
        var id = option.Host.Id;
        var last = option.Host;
        try
        {
            await _watcher.WatchAsync(() => last = HostById(id)?.Host ?? last, e => OnHostEventAsync(id, e), watch.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Watching host {Address} failed", last.Address);
        }
        finally
        {
            // Ended by itself (e.g. a refused token): a later change of the host may start it again.
            if (_watches.TryGetValue(id, out var current) && current == watch)
            {
                _watches.Remove(id);
            }

            watch.Dispose();
        }
    }

    private HostOption? HostById(Guid id) => Hosts.FirstOrDefault(h => h.Host.Id == id);

    private async Task OnHostEventAsync(Guid id, HostEvent change)
    {
        if (HostById(id) is not { } option)
        {
            return;
        }

        if (change == HostEvent.Unreachable)
        {
            option.IsOffline = true;
            return;
        }

        if (!ReferenceEquals(option, SelectedHost))
        {
            await SyncQuietlyAsync(option);
            return;
        }

        // The selected host: its tiles and the status bar update like F5 (without its dialogs).
        var waited = TimeSpan.Zero;
        while (!CanRefresh() && waited < BusyWait)
        {
            await Task.Delay(500);
            waited += TimeSpan.FromMilliseconds(500);
        }

        if (ReferenceEquals(option, SelectedHost) && CanRefresh())
        {
            await SyncAsync(interactive: false);
        }
    }

    /// <summary>A host that is not on screen: only its card and cache change.</summary>
    private async Task SyncQuietlyAsync(HostOption option)
    {
        try
        {
            var synced = await _sync.SyncAsync(option.Host);
            if (HostById(synced.Id) is null)
            {
                return; // removed meanwhile
            }

            option.IsOffline = false;
            ReplaceHost(synced);
        }
        catch (AgentUnreachableException)
        {
            option.IsOffline = true;
        }
        catch (InvalidTokenException ex) when (ex.RefusedByHost)
        {
            _logger.LogWarning(ex, "{Address} refused this computer's token", option.Host.Address);
            ForgetRemovedHost(option, interactive: false);
        }
        catch (Exception ex) when (ex is AgentException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Background sync of {Address} failed", option.Host.Address);
        }
    }
}
