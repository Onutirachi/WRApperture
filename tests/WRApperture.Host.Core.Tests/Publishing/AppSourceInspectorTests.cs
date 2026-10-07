using WRApperture.Host.Core.Discovery;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Publishing;
using WRApperture.Host.Core.Tests.Fakes;

namespace WRApperture.Host.Core.Tests.Publishing;

public class AppSourceInspectorTests
{
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string Notepad = @"C:\Windows\System32\notepad.exe";

    private readonly FakeFileSystemProbe _fs = new();
    private readonly FakeResolver _resolver = new();
    private readonly FakeDescriptions _descriptions = new();
    private readonly AppSourceInspector _inspector;

    public AppSourceInspectorTests()
    {
        _fs.Files.UnionWith([Chrome, Notepad]);
        _inspector = new AppSourceInspector(_resolver, _fs, _descriptions);
    }

    [Fact]
    public void Shortcut_gives_name_target_arguments_and_icon()
    {
        const string lnk = @"C:\Users\joao\Desktop\Chrome Trabalho.lnk";
        const string ico = @"C:\Program Files\Google\Chrome\Application\chrome_proxy.exe";
        _fs.Files.UnionWith([lnk, ico]);
        _resolver.Targets[lnk] = new ShortcutTarget(Chrome, "--profile-directory=\"Profile 1\"", ico, 2);

        var draft = _inspector.Inspect(lnk);

        Assert.Equal("Chrome Trabalho", draft.DisplayName);
        Assert.Equal(Chrome, draft.ExecutablePath);
        Assert.Equal("--profile-directory=\"Profile 1\"", draft.Arguments);
        Assert.Equal((ico, 2), (draft.IconPath, draft.IconIndex));
    }

    [Fact]
    public void Shortcut_icon_falls_back_to_the_executable()
    {
        const string lnk = @"C:\x\Notepad.lnk";
        _fs.Files.Add(lnk);
        _resolver.Targets[lnk] = new ShortcutTarget(Notepad, null, @"C:\missing.ico", 4);

        var draft = _inspector.Inspect(lnk);

        Assert.Equal((Notepad, 0), (draft.IconPath, draft.IconIndex));
        Assert.Null(draft.Arguments);
    }

    [Fact]
    public void Executable_uses_file_description_as_name()
    {
        _descriptions.Values[Chrome] = "  Google Chrome  ";

        var draft = _inspector.Inspect($"\"{Chrome}\"");

        Assert.Equal("Google Chrome", draft.DisplayName);
        Assert.Equal(Chrome, draft.ExecutablePath);
        Assert.Equal((Chrome, 0), (draft.IconPath, draft.IconIndex));
    }

    [Fact]
    public void Executable_without_description_uses_file_name()
    {
        Assert.Equal("notepad", _inspector.Inspect(Notepad).DisplayName);
    }

    [Fact]
    public void Environment_variables_are_expanded()
    {
        Assert.Equal(Notepad, _inspector.Inspect(@"%SystemRoot%\System32\notepad.exe").ExecutablePath);
    }

    [Theory]
    [InlineData(@"C:\docs\manual.pdf", "programas")]
    [InlineData(@"C:\scripts\run.bat", "programas")]
    [InlineData(@"C:\nao\existe.exe", "não encontrado")]
    public void Rejects_non_programs_and_missing_files(string path, string reason)
    {
        _fs.Files.UnionWith([@"C:\docs\manual.pdf", @"C:\scripts\run.bat"]);

        var ex = Assert.Throws<AppValidationException>(() => _inspector.Inspect(path));

        Assert.Contains(reason, ex.Reason);
    }

    [Fact]
    public void Rejects_unreadable_or_broken_shortcuts()
    {
        const string unreadable = @"C:\x\Weird.lnk";
        const string broken = @"C:\x\Old.lnk";
        _fs.Files.UnionWith([unreadable, broken]);
        _resolver.Targets[broken] = new ShortcutTarget(@"C:\gone\app.exe", null, null, 0);

        Assert.Contains("não pôde ser lido", Assert.Throws<AppValidationException>(() => _inspector.Inspect(unreadable)).Reason);
        Assert.Contains("não existe", Assert.Throws<AppValidationException>(() => _inspector.Inspect(broken)).Reason);
    }

    [Fact]
    public void Rejects_shortcuts_to_scripts_documents_and_msi_icon_stubs()
    {
        const string script = @"C:\x\Script.lnk";
        const string stub = @"C:\x\Office.lnk";
        const string stubExe = @"C:\Windows\Installer\{1}\icon.exe";
        _fs.Files.UnionWith([script, stub, stubExe, @"C:\x\run.cmd"]);
        _resolver.Targets[script] = new ShortcutTarget(@"C:\x\run.cmd", null, null, 0);
        _resolver.Targets[stub] = new ShortcutTarget(stubExe, null, null, 0);

        Assert.Contains("programas", Assert.Throws<AppValidationException>(() => _inspector.Inspect(script)).Reason);
        Assert.Contains("instalador", Assert.Throws<AppValidationException>(() => _inspector.Inspect(stub)).Reason);
    }

    [Fact]
    public void Long_names_are_truncated_to_the_publishing_limit()
    {
        _descriptions.Values[Chrome] = new string('x', 300);

        Assert.Equal(AppValidator.MaxDisplayName, _inspector.Inspect(Chrome).DisplayName.Length);
    }

    private sealed class FakeResolver : IShortcutResolver
    {
        public Dictionary<string, ShortcutTarget> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ShortcutTarget? Resolve(string shortcutPath) => Targets.GetValueOrDefault(shortcutPath);
    }

    private sealed class FakeDescriptions : IFileDescriptionReader
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Describe(string executablePath) => Values.GetValueOrDefault(executablePath);
    }
}
