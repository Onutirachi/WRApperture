using Microsoft.Win32;
using WRApperture.Host.Core.Discovery;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.StartWith;
using WRApperture.Host.Core.Tests.Fakes;

namespace WRApperture.Host.Core.Tests.StartWith;

public class StartupProgramsTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private readonly FakeRegistryAccess _registry = new();
    private readonly FakeUserRegistryAccess _users = new();
    private readonly FakeFileSystemProbe _fs = new();
    private readonly FakeLinks _links = new();

    private StartupPrograms Create(params string[] startupFolder) =>
        new(_registry, _users, _fs, _links) { StartupFolderShortcuts = () => startupFolder };

    [Fact]
    public void Reads_machine_32_bit_and_user_run_keys()
    {
        _registry.Seed(StartupPrograms.Run, "FortiClient", @"""C:\Program Files\Fortinet\FortiClient\FortiTray.exe""");
        _registry.Seed(StartupPrograms.Run32, "Steam", @"""C:\Program Files (x86)\Steam\steam.exe"" -silent");
        _users.SetValue(Sid, StartupPrograms.Run, "Discord", @"C:\Users\joao\AppData\Local\Discord\Update.exe --processStart Discord.exe",
            RegistryValueKind.String);

        var programs = Create().List(Sid);

        Assert.Contains(new StartupProgram("FortiClient", @"C:\Program Files\Fortinet\FortiClient\FortiTray.exe", null), programs);
        Assert.Contains(new StartupProgram("Steam", @"C:\Program Files (x86)\Steam\steam.exe", "-silent"), programs);
        Assert.Contains(new StartupProgram("Discord", @"C:\Users\joao\AppData\Local\Discord\Update.exe", "--processStart Discord.exe"), programs);
        Assert.Equal(2, Create().List(null).Count); // without a user, only the machine's
    }

    [Fact]
    public void Skips_entries_turned_off_in_task_manager_and_lines_that_are_not_programs()
    {
        _registry.Seed(StartupPrograms.Run, "On", @"C:\A\on.exe");
        _registry.Seed(StartupPrograms.Run, "Off", @"C:\A\off.exe");
        _registry.Seed(StartupPrograms.ApprovedRun, "On", new byte[] { 2, 0, 0, 0 }, RegistryValueKind.Binary);
        _registry.Seed(StartupPrograms.ApprovedRun, "Off", new byte[] { 3, 0, 0, 0 }, RegistryValueKind.Binary);
        _registry.Seed(StartupPrograms.Run, "Script", @"wscript.exe C:\A\start.vbs");
        _registry.Seed(StartupPrograms.Run, "Batch", @"C:\A\start.cmd");

        var names = Create().List(null).Select(p => p.Name);

        Assert.Equal(["On"], names);
    }

    [Fact]
    public void Expands_variables_and_reads_the_startup_folder()
    {
        _fs.Env["ProgramFiles"] = @"C:\Program Files";
        _registry.Seed(StartupPrograms.Run, "Tray", @"%ProgramFiles%\Vendor\tray.exe /hidden");
        _links.Targets[@"C:\Startup\Agent.lnk"] = new ShortcutTarget(@"C:\Program Files\Vendor\agent.exe", "-q", null, 0);
        _links.Targets[@"C:\Startup\Off.lnk"] = new ShortcutTarget(@"C:\Program Files\Vendor\off.exe", null, null, 0);
        _registry.Seed(StartupPrograms.ApprovedFolder, "Off.lnk", new byte[] { 3 }, RegistryValueKind.Binary);

        var programs = Create(@"C:\Startup\Agent.lnk", @"C:\Startup\Off.lnk", @"C:\Startup\Broken.lnk").List(null);

        Assert.Equal(
            [
                new StartupProgram("Tray", @"C:\Program Files\Vendor\tray.exe", "/hidden"),
                new StartupProgram("Agent", @"C:\Program Files\Vendor\agent.exe", "-q"),
            ],
            programs);
    }

    internal sealed class FakeLinks : IShortcutResolver
    {
        public Dictionary<string, ShortcutTarget> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ShortcutTarget? Resolve(string shortcutPath) => Targets.GetValueOrDefault(shortcutPath);
    }
}

public class StartWithResolverTests
{
    private const string FortiClient = @"C:\Program Files\Fortinet\FortiClient\FortiClient.exe";
    private const string FortiTray = @"C:\Program Files\Fortinet\FortiClient\FortiTray.exe";
    private readonly FakeStartup _startup = new();
    private readonly StartWithResolver _resolver;

    public StartWithResolverTests() => _resolver = new StartWithResolver(_startup);

    private static PublishedApp App(string path, IReadOnlyList<string>? startWith = null, bool auto = true) =>
        new("app", "App", path, null, path, 0, CommandLinePolicy.None, null, true, true, startWith, auto);

    [Fact]
    public void Startup_programs_of_the_apps_folder_start_with_it()
    {
        _startup.Programs.Add(new StartupProgram("FortiClient", FortiTray, null));
        _startup.Programs.Add(new StartupProgram("Steam", @"C:\Program Files (x86)\Steam\steam.exe", "-silent"));

        var programs = _resolver.For(App(FortiClient), "sid");

        var tray = Assert.Single(programs);
        Assert.Equal(FortiTray, tray.ExecutablePath);
        Assert.True(tray.Automatic);
        Assert.Equal("sid", _startup.LastSid);
    }

    [Theory]
    [InlineData(@"C:\Program Files\LGHUB\lghub.exe", @"C:\Program Files\LGHUB\system_tray\lghub_system_tray.exe", true)]  // helper in a subfolder
    [InlineData(@"C:\Program Files\Vendor\bin\app.exe", @"C:\Program Files\Vendor\tray.exe", true)]                         // app in a subfolder
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", @"C:\Program Files\Google\Drive\drive.exe", false)] // siblings
    [InlineData(@"C:\Program Files\app.exe", @"C:\Program Files\Vendor\tray.exe", false)]                                   // too general
    [InlineData(@"C:\Tools\app.exe", @"C:\Tools\tray.exe", true)]                                                           // same folder
    public void Folders_match_when_one_contains_the_other_below_the_drive(string app, string program, bool matches)
    {
        _startup.Programs.Add(new StartupProgram("Tray", program, null));

        Assert.Equal(matches, _resolver.Detect(app, null).Count == 1);
    }

    [Fact]
    public void Nothing_from_the_windows_folder_comes_along()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        _startup.Programs.Add(new StartupProgram("SecurityHealth", Path.Combine(windows, @"System32\SecurityHealthSystray.exe"), null));

        Assert.Empty(_resolver.Detect(Path.Combine(windows, @"System32\notepad.exe"), null));
    }

    [Fact]
    public void The_app_itself_is_not_started_twice()
    {
        _startup.Programs.Add(new StartupProgram("FortiClient", FortiClient, "-tray"));

        Assert.Empty(_resolver.Detect(FortiClient, null));
    }

    [Fact]
    public void Chosen_programs_come_first_and_automatic_ones_can_be_turned_off()
    {
        _startup.Programs.Add(new StartupProgram("FortiClient", FortiTray, "-x"));
        const string Extra = @"D:\Tools\helper.exe";

        var both = _resolver.For(App(FortiClient, [Extra, FortiTray]), null);
        Assert.Equal([Extra, FortiTray], both.Select(p => p.ExecutablePath));
        Assert.All(both, p => Assert.False(p.Automatic)); // FortiTray chosen by hand wins over the detected copy

        var manualOnly = _resolver.For(App(FortiClient, [Extra], auto: false), null);
        Assert.Equal([Extra], manualOnly.Select(p => p.ExecutablePath));
    }

    private sealed class FakeStartup : IStartupPrograms
    {
        public List<StartupProgram> Programs { get; } = [];
        public string? LastSid { get; private set; }

        public IReadOnlyList<StartupProgram> List(string? userSid)
        {
            LastSid = userSid;
            return Programs;
        }
    }
}
