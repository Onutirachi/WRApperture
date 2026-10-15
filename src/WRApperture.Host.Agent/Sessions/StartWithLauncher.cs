using System.ComponentModel;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Sessions;
using WRApperture.Host.Core.StartWith;

namespace WRApperture.Host.Agent.Sessions;

/// <summary>
/// Remote app sessions have no Explorer, so Windows does not start the sign-in programs there. Some apps depend on
/// them (FortiClient's sign-in window comes from FortiTray). When a published app runs in a remote app session,
/// this starts the programs that go with it (<see cref="IStartWithResolver"/>) in that session, once per session.
/// <para>
/// Programs without arguments are opened through Explorer ("explorer.exe program"), like a double click: some only
/// work when Explorer starts them (FortiTray shows the sign-in but cannot bring the VPN up otherwise). That Explorer
/// is ended as soon as the program appears (or after <see cref="ExplorerGrace"/>): a session with Explorer counts as
/// a desktop session, which the next app opening would log off.
/// </para>
/// </summary>
internal sealed class StartWithLauncher(
    ISessionProvider sessions,
    IProcessList processes,
    IAppPublisher publisher,
    IStartWithResolver resolver,
    IHostEnvironment environment,
    TimeProvider time,
    ILogger<StartWithLauncher> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ExplorerGrace = TimeSpan.FromSeconds(15);
    internal static readonly string ExplorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>Explorers started to open a program, ended once it runs.</summary>
    private readonly List<(int Session, int ExplorerId, string Program, DateTimeOffset StartedAt)> _launchers = [];

    private readonly HashSet<(int Session, string Program)> _started = [];
    private readonly Dictionary<(string Alias, string? Sid), IReadOnlyList<StartWithProgram>> _programs = [];
    private IReadOnlyList<PublishedApp> _apps = [];
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing"))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogWarning(ex, "Start-with check failed");
            }

            try
            {
                await Task.Delay(Interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One pass over the remote app sessions (also called by tests).</summary>
    internal void Tick()
    {
        var all = sessions.List();
        EndLaunchers(all);
        var remote = all.Where(s => s is { Kind: SessionKind.RemoteApp, State: SessionConnectState.Active }).ToList();
        _started.RemoveWhere(started => remote.All(s => s.Id != started.Session)); // sessions that ended
        if (remote.Count == 0)
        {
            return;
        }

        RefreshCache();
        var apps = _apps.Where(a => a.AutoStartWith || a.StartWithPrograms.Count > 0).ToList();
        if (apps.Count == 0)
        {
            return;
        }

        var running = processes.InSessions(remote.Select(s => s.Id).ToHashSet());
        foreach (var session in remote)
        {
            var paths = running.Where(p => p.SessionId == session.Id && p.Path is not null)
                .Select(p => p.Path!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var app in apps.Where(a => paths.Contains(a.ExecutablePath)))
            {
                foreach (var program in ProgramsFor(app, session.Sid))
                {
                    if (paths.Contains(program.ExecutablePath) || !_started.Add((session.Id, program.ExecutablePath)))
                    {
                        continue; // already running, or already started once in this session
                    }

                    Start(session, app, program);
                }
            }
        }
    }

    private void Start(HostSession session, PublishedApp app, StartWithProgram program)
    {
        try
        {
            if (program.Arguments is null)
            {
                var explorer = sessions.StartProcess(session.Id, ExplorerPath, $"\"{program.ExecutablePath}\"");
                _launchers.Add((session.Id, explorer, program.ExecutablePath, time.GetUtcNow()));
            }
            else
            {
                sessions.StartProcess(session.Id, program.ExecutablePath, program.Arguments);
            }

            logger.LogInformation("Started {Program} with {App} in session {Session} of {User} ({How})",
                program.ExecutablePath, app.Alias, session.Id, session.User, program.Automatic ? "startup program of its folder" : "chosen");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not start {Program} with {App} in session {Session}", program.ExecutablePath, app.Alias, session.Id);
        }
    }

    /// <summary>Ends the Explorers that opened a program once it runs (or the grace time is over).</summary>
    private void EndLaunchers(IReadOnlyList<HostSession> all)
    {
        if (_launchers.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        var running = processes.InSessions(_launchers.Select(l => l.Session).ToHashSet());
        foreach (var launcher in _launchers.ToList())
        {
            var started = running.Any(p => p.SessionId == launcher.Session &&
                                           string.Equals(p.Path, launcher.Program, StringComparison.OrdinalIgnoreCase));
            var sessionGone = all.All(s => s.Id != launcher.Session);
            if (!started && !sessionGone && now - launcher.StartedAt < ExplorerGrace)
            {
                continue;
            }

            _launchers.Remove(launcher);
            if (!sessionGone && processes.Stop(launcher.ExplorerId, "explorer"))
            {
                logger.LogInformation("Ended the Explorer ({Pid}) that opened {Program} in session {Session}",
                    launcher.ExplorerId, launcher.Program, launcher.Session);
            }
        }
    }

    private IReadOnlyList<StartWithProgram> ProgramsFor(PublishedApp app, string? sid)
    {
        if (!_programs.TryGetValue((app.Alias, sid), out var programs))
        {
            programs = resolver.For(app, sid);
            _programs[(app.Alias, sid)] = programs;
        }

        return programs;
    }

    /// <summary>Published apps and startup programs change rarely: re-read them every <see cref="CacheFor"/>.</summary>
    private void RefreshCache()
    {
        var now = time.GetUtcNow();
        if (now - _cachedAt < CacheFor)
        {
            return;
        }

        _apps = publisher.List();
        _programs.Clear();
        _cachedAt = now;
    }
}
