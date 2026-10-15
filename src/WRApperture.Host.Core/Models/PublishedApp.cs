namespace WRApperture.Host.Core.Models;

/// <param name="StartWith">Programs the agent also starts in the remote session when this app opens.</param>
/// <param name="AutoStartWith">
/// Also start the programs that start with Windows from this app's own folder (e.g. a VPN's tray helper that
/// shows its sign-in window). Remote app sessions have no Explorer, so Windows does not start them there.
/// </param>
public sealed record PublishedApp(
    string Alias,
    string DisplayName,
    string ExecutablePath,
    string? RawPath,
    string IconPath,
    int IconIndex,
    CommandLinePolicy CommandLinePolicy,
    string? RequiredArguments,
    bool ShowInList,
    bool IsManaged,
    IReadOnlyList<string>? StartWith = null,
    bool AutoStartWith = true)
{
    public IReadOnlyList<string> StartWithPrograms => StartWith ?? [];
}
