using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WRApperture.Host.Agent.Sessions;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Sessions;
using WRApperture.Host.Core.StartWith;

namespace WRApperture.Host.Agent.Tests;

public class StartWithTests : IDisposable
{
    private const string Forti = @"C:\Secret\forti.exe";
    private const string Tray = @"C:\Secret\FortiTray.exe";
    private readonly AgentFactory _factory = new();

    public StartWithTests()
    {
        _factory.Publisher.Apps.Add(FakePublisher.App("forti", "FortiClient"));
        _factory.Startup.Programs.Add(new StartupProgram("FortiClient", Tray, "-tray"));
    }

    public void Dispose() => _factory.Dispose();

    private StartWithLauncher Launcher => _factory.Services.GetServices<IHostedService>().OfType<StartWithLauncher>().Single();

    private void Session(int id, SessionKind kind) =>
        _factory.Sessions.All.Add(new HostSession(id, "joao", "PC", SessionConnectState.Active, false, kind, "S-1-5-21-1"));

    private void Running(int session, string path) => _factory.Processes.All.Add(new RunningProcess(_factory.Processes.All.Count + 100, session, path));

    [Fact]
    public void Starts_the_apps_startup_program_once_in_its_remote_app_session()
    {
        Session(3, SessionKind.RemoteApp);
        Running(3, Forti);

        Launcher.Tick();
        Launcher.Tick(); // still not running (e.g. it exited): not started again in this session

        var started = Assert.Single(_factory.Sessions.Started);
        Assert.Equal((3, Tray, "-tray"), (started.Session, started.Program, started.Arguments));
    }

    [Fact]
    public void Nothing_starts_when_it_already_runs_or_the_app_is_closed()
    {
        Session(3, SessionKind.RemoteApp);
        Running(3, Forti);
        Running(3, Tray);
        Session(4, SessionKind.RemoteApp); // FortiClient not open here

        Launcher.Tick();

        Assert.Empty(_factory.Sessions.Started);
    }

    [Fact]
    public void Desktop_sessions_are_left_to_explorer()
    {
        Session(3, SessionKind.Desktop);
        Running(3, Forti);

        Launcher.Tick();

        Assert.Empty(_factory.Sessions.Started);
    }

    [Fact]
    public void Turned_off_apps_only_start_the_programs_chosen_for_them()
    {
        const string Helper = @"D:\Tools\helper.exe";
        _factory.Publisher.Apps[0] = _factory.Publisher.Apps[0] with { AutoStartWith = false, StartWith = [Helper] };
        Session(3, SessionKind.RemoteApp);
        Running(3, Forti);

        Launcher.Tick();

        Assert.Equal([Helper], _factory.Sessions.Started.Select(s => s.Program));
    }

    [Fact]
    public void A_new_session_gets_the_program_again()
    {
        Session(3, SessionKind.RemoteApp);
        Running(3, Forti);
        Launcher.Tick();

        _factory.Sessions.All.Clear(); // logged off
        Launcher.Tick();
        Session(3, SessionKind.RemoteApp); // Windows reuses the id
        Launcher.Tick();

        Assert.Equal(2, _factory.Sessions.Started.Count);
    }
}
