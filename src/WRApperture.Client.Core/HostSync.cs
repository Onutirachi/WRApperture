using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Storage;

namespace WRApperture.Client.Core;

/// <summary>Refreshes a host's app list, RDP port and icon cache from its agent.</summary>
public sealed class HostSync(IconStore icons, Func<PairedHost, IAgentApi> clientFactory, TimeProvider time, ILogger<HostSync>? logger = null)
{
    /// <summary>One size serves the tiles (scaled down) and desktop shortcut icons.</summary>
    public const int IconSize = 256;

    private readonly ILogger _logger = logger ?? NullLogger<HostSync>.Instance;

    public async Task<PairedHost> SyncAsync(PairedHost host, CancellationToken ct = default)
    {
        using var client = clientFactory(host);
        var info = await client.GetHostInfoAsync(ct);
        var apps = await client.GetAppsAsync(ct);
        var previous = host.Apps.ToDictionary(a => a.Alias, StringComparer.OrdinalIgnoreCase);

        var cached = new List<CachedApp>(apps.Count);
        foreach (var app in apps)
        {
            var etag = previous.TryGetValue(app.Alias, out var old) && icons.Exists(host.Id, app.Alias) ? old.IconETag : null;
            try
            {
                var icon = await client.GetIconAsync(app.Alias, IconSize, etag, ct);
                if (!icon.NotModified && icon.Png is { Length: > 0 })
                {
                    icons.Write(host.Id, app.Alias, icon.Png);
                }

                etag = icon.ETag;
            }
            catch (AgentException ex) when (ex is not InvalidTokenException and not CertificateChangedException)
            {
                // A missing icon must not hide the app; keep whatever is cached.
                _logger.LogWarning(ex, "Icon for {Alias} unavailable", app.Alias);
            }

            // Local names stay with apps that are still published (and leave with removed ones).
            cached.Add(new CachedApp(app.Alias, app.DisplayName, etag).WithCustomName(old?.CustomName));
        }

        _logger.LogInformation("Synced host {Host}: {Count} apps", host.Address, cached.Count);
        return host with { Apps = cached, RdpPort = info.RdpPort, LastSyncUtc = time.GetUtcNow() };
    }
}
