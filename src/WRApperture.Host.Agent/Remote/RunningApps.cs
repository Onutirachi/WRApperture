using WRApperture.Host.Core;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Sessions;

namespace WRApperture.Host.Agent.Remote;

/// <summary>
/// Published apps open in RemoteApp sessions, matched by executable path: what the clients mark as open and can close.
/// Desktop sessions (whoever uses the PC itself) are never looked at, so their programs are never closed from here.
/// Apps started through a launcher that exits (another .exe stays running) are not recognized.
/// </summary>
internal sealed class RunningApps(ISessionProvider sessions, IProcessList processes, ILogger<RunningApps> logger)
{
    /// <summary>The processes of each published app (by alias) in RemoteApp sessions.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RunningProcess>> Find(IReadOnlyList<PublishedApp> apps)
    {
        var result = new Dictionary<string, IReadOnlyList<RunningProcess>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var remote = sessions.List().Where(s => s.Kind == SessionKind.RemoteApp).Select(s => s.Id).ToHashSet();
            if (remote.Count == 0)
            {
                return result;
            }

            var running = processes.InSessions(remote).Where(p => p.Path is not null).ToList();
            foreach (var app in apps)
            {
                var mine = running.Where(p => string.Equals(p.Path, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (mine.Count > 0)
                {
                    result[app.Alias] = mine;
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not list the remote apps' processes");
        }

        return result;
    }

    public IReadOnlyList<RunningProcess> Of(PublishedApp app) =>
        Find([app]).TryGetValue(app.Alias, out var mine) ? mine : [];
}
