namespace WRApperture.Host.Core.Models;

public enum DiscoverySource
{
    AllUsers,
    CurrentUser,
}

public sealed record DiscoveredApp(
    string DisplayName,
    string ExecutablePath,
    string? Arguments,
    string IconPath,
    int IconIndex,
    DiscoverySource Source,
    string? PublishedAlias);
