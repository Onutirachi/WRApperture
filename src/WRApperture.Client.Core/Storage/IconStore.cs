namespace WRApperture.Client.Core.Storage;

/// <summary>On-disk icon cache per host; lets the grid render instantly and while the host is offline.</summary>
public sealed class IconStore(ClientPaths paths)
{
    public string PngPath(Guid hostId, string alias) =>
        Path.Combine(paths.HostIcons(hostId), ClientPaths.SafeFileName(alias) + ".png");

    /// <summary>
    /// Writes a shortcut icon named after its content ("&lt;alias&gt;-&lt;hash&gt;.ico"). Windows caches shortcut
    /// icons by file path, so a changed icon (e.g. the WRApperture badge added) needs a new path to show up
    /// when the shortcut is recreated. Older versions stay: other shortcuts may still point at them.
    /// </summary>
    public string WriteIco(Guid hostId, string alias, byte[] ico)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ico))[..12].ToLowerInvariant();
        var path = Path.Combine(paths.HostIcons(hostId), $"{ClientPaths.SafeFileName(alias)}-{hash}.ico");
        Directory.CreateDirectory(paths.HostIcons(hostId));
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, ico);
        }

        return path;
    }

    public bool Exists(Guid hostId, string alias) => File.Exists(PngPath(hostId, alias));

    public void Write(Guid hostId, string alias, byte[] png)
    {
        Directory.CreateDirectory(paths.HostIcons(hostId));
        File.WriteAllBytes(PngPath(hostId, alias), png);
    }

    /// <summary>Removes cached icons and .rdp files of a host.</summary>
    public void DeleteHostFiles(Guid hostId)
    {
        foreach (var dir in new[] { paths.HostIcons(hostId), paths.HostRdp(hostId) })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
