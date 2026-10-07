namespace WRApperture.Client.Core.Storage;

/// <summary>On-disk icon cache per host; lets the grid render instantly and while the host is offline.</summary>
public sealed class IconStore(ClientPaths paths)
{
    public string PngPath(Guid hostId, string alias) =>
        Path.Combine(paths.HostIcons(hostId), ClientPaths.SafeFileName(alias) + ".png");

    public string IcoPath(Guid hostId, string alias) =>
        Path.Combine(paths.HostIcons(hostId), ClientPaths.SafeFileName(alias) + ".ico");

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
