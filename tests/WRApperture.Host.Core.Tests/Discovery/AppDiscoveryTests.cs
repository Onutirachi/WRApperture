using Microsoft.Extensions.Logging.Abstractions;
using WRApperture.Host.Core.Discovery;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Publishing;
using WRApperture.Host.Core.Tests.Fakes;

namespace WRApperture.Host.Core.Tests.Discovery;

public class AppDiscoveryTests
{
    private const string Excel = @"C:\Program Files\Office\EXCEL.EXE";
    private const string Notepad = @"C:\Windows\System32\notepad.exe";

    private readonly FakeShortcuts _shortcuts = new();
    private readonly FakeFileSystemProbe _fs = new();
    private readonly FakeRegistryAccess _registry = new();
    private readonly AppPublisher _publisher;
    private readonly AppDiscovery _discovery;

    public AppDiscoveryTests()
    {
        _fs.Files.UnionWith([Excel, Notepad]);
        _publisher = new AppPublisher(_registry, new FakePrivilegeChecker(), _fs, NullLogger<AppPublisher>.Instance);
        _discovery = new AppDiscovery(_shortcuts, _shortcuts, _fs, _publisher, NullLogger<AppDiscovery>.Instance);
    }

    [Fact]
    public void Lists_resolved_executables_sorted_by_name()
    {
        _shortcuts.Add(@"AllUsers\Notepad.lnk", Notepad);
        _shortcuts.Add(@"AllUsers\Office\Excel.lnk", Excel);

        var apps = _discovery.Discover();

        Assert.Equal(["Excel", "Notepad"], apps.Select(a => a.DisplayName));
        Assert.Equal(Excel, apps[0].ExecutablePath);
        Assert.Equal(DiscoverySource.AllUsers, apps[0].Source);
    }

    [Fact]
    public void Skips_broken_unresolvable_and_non_exe_targets()
    {
        _fs.Files.Add(@"C:\docs\manual.pdf");
        _shortcuts.Add(@"AllUsers\Broken.lnk", @"C:\gone\app.exe");
        _shortcuts.Add(@"AllUsers\Manual.lnk", @"C:\docs\manual.pdf");
        _shortcuts.Add(@"AllUsers\Site.lnk", target: null);
        _shortcuts.Add(@"AllUsers\Notepad.lnk", Notepad);

        Assert.Equal(["Notepad"], _discovery.Discover().Select(a => a.DisplayName));
    }

    [Theory]
    [InlineData(@"C:\Program Files\App\unins000.exe", "App")]
    [InlineData(@"C:\Program Files\App\Uninstall.exe", "App")]
    [InlineData(@"C:\Program Files\App\setup.exe", "App")]
    [InlineData(@"C:\Program Files\App\app.exe", "Uninstall App")]
    [InlineData(@"C:\Program Files\App\app.exe", "Desinstalar App")]
    public void Skips_uninstallers_and_installers(string exe, string name)
    {
        _fs.Files.Add(exe);
        _shortcuts.Add($@"AllUsers\{name}.lnk", exe);

        Assert.Empty(_discovery.Discover());
    }

    [Fact]
    public void Skips_unresolved_msi_icon_stubs()
    {
        const string stub = @"C:\Windows\Installer\{0D490D76}\NewShortcut1_ABC.exe";
        _fs.Files.Add(stub);
        _shortcuts.Add(@"AllUsers\CorelDRAW.lnk", stub);

        Assert.Empty(_discovery.Discover());
    }

    [Fact]
    public void Deduplicates_same_target_preferring_all_users()
    {
        _shortcuts.Add(@"CurrentUser\Excel (meu).lnk", Excel, source: DiscoverySource.CurrentUser);
        _shortcuts.Add(@"AllUsers\Excel.lnk", Excel);

        var app = Assert.Single(_discovery.Discover());

        Assert.Equal("Excel", app.DisplayName);
        Assert.Equal(DiscoverySource.AllUsers, app.Source);
    }

    [Fact]
    public void Same_exe_with_different_arguments_are_distinct()
    {
        _shortcuts.Add(@"AllUsers\Excel.lnk", Excel);
        _shortcuts.Add(@"AllUsers\Excel Safe.lnk", Excel, args: "/safe");

        Assert.Equal(2, _discovery.Discover().Count);
    }

    [Fact]
    public void Marks_already_published_apps()
    {
        _publisher.Publish(new PublishRequest(Excel, "Planilhas"));
        _shortcuts.Add(@"AllUsers\Excel.lnk", Excel);
        _shortcuts.Add(@"AllUsers\Notepad.lnk", Notepad);

        var apps = _discovery.Discover();

        Assert.Equal("planilhas", apps.Single(a => a.DisplayName == "Excel").PublishedAlias);
        Assert.Null(apps.Single(a => a.DisplayName == "Notepad").PublishedAlias);
    }

    [Fact]
    public void Uses_shortcut_icon_when_it_exists_otherwise_the_exe()
    {
        const string ico = @"C:\Program Files\Office\xl.ico";
        _fs.Files.Add(ico);
        _shortcuts.Add(@"AllUsers\Excel.lnk", Excel, iconPath: ico, iconIndex: 3);
        _shortcuts.Add(@"AllUsers\Notepad.lnk", Notepad, iconPath: @"C:\missing.ico", iconIndex: 5);

        var apps = _discovery.Discover();

        var excel = apps.Single(a => a.DisplayName == "Excel");
        Assert.Equal((ico, 3), (excel.IconPath, excel.IconIndex));
        var notepad = apps.Single(a => a.DisplayName == "Notepad");
        Assert.Equal((Notepad, 0), (notepad.IconPath, notepad.IconIndex));
    }

    private sealed class FakeShortcuts : IStartMenuShortcuts, IShortcutResolver
    {
        private readonly List<ShortcutFile> _files = [];
        private readonly Dictionary<string, ShortcutTarget?> _targets = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string lnk, string? target, string? args = null, string? iconPath = null, int iconIndex = 0,
            DiscoverySource source = DiscoverySource.AllUsers)
        {
            _files.Add(new ShortcutFile(lnk, source));
            _targets[lnk] = target is null ? null : new ShortcutTarget(target, args, iconPath, iconIndex);
        }

        public IEnumerable<ShortcutFile> Enumerate() => _files;

        public ShortcutTarget? Resolve(string shortcutPath) => _targets[shortcutPath];
    }
}
