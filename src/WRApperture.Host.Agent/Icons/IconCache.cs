using System.Collections.Concurrent;
using System.Security.Cryptography;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Agent.Icons;

internal sealed record CachedIcon(byte[] Png, string ETag);

/// <summary>
/// Caches rendered icons. The key includes the icon file's last write time, so replacing
/// the executable (e.g. an app update) produces a new icon and a new ETag.
/// </summary>
internal sealed class IconCache(IIconProvider icons)
{
    private const int MaxEntries = 512;
    private readonly ConcurrentDictionary<(string Alias, string Path, int Index, int Size, DateTime Stamp), CachedIcon> _cache = new();

    public CachedIcon Get(PublishedApp app, int size)
    {
        var path = Environment.ExpandEnvironmentVariables(app.IconPath);
        var key = (app.Alias.ToLowerInvariant(), path.ToLowerInvariant(), app.IconIndex, size, File.GetLastWriteTimeUtc(path));
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (_cache.Count >= MaxEntries)
        {
            _cache.Clear();
        }

        var png = icons.GetPng(app.IconPath, app.IconIndex, size);
        var etag = $"\"{Convert.ToHexString(SHA256.HashData(png))[..16]}\"";
        return _cache[key] = new CachedIcon(png, etag);
    }
}
