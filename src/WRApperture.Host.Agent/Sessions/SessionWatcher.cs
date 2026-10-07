using WRApperture.Host.Core.Sessions;

namespace WRApperture.Host.Agent.Sessions;

/// <summary>
/// When the same user logs on at the host while their RemoteApp session exists, Windows attaches the
/// host screen to that shell-less session. This watcher gives it a desktop (explorer.exe) so local use
/// works and the apps that were open remotely stay open, without losing work.
/// </summary>
internal sealed class SessionWatcher(ISessionProvider sessions, TimeProvider time, ILogger<SessionWatcher> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly HashSet<int> _handled = [];

    /// <summary>One polling step; returns the session given a desktop, if any. Exposed for tests.</summary>
    public int? Tick()
    {
        var all = sessions.List();
        _handled.IntersectWith(all.Select(s => s.Id)); // forget sessions that ended (ids are reused)

        var target = SessionPolicy.ConsoleOnRemoteApp(all);
        if (target is null || !_handled.Add(target.Id))
        {
            return null;
        }

        logger.LogWarning("Host screen attached to RemoteApp session {Session} of {User}; starting desktop shell", target.Id, target.User);
        try
        {
            sessions.StartDesktopShell(target.Id);
            return target.Id;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogError(ex, "Could not start desktop shell in session {Session}", target.Id);
            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Tick();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                logger.LogWarning(ex, "Session watcher tick failed");
            }
        }
    }
}
