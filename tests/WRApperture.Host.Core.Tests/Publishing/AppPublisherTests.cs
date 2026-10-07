using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Publishing;
using WRApperture.Host.Core.Tests.Fakes;

namespace WRApperture.Host.Core.Tests.Publishing;

public class AppPublisherTests
{
    private const string Notepad = @"C:\Windows\System32\notepad.exe";
    private const string Calc = @"C:\Windows\System32\calc.exe";

    private readonly FakeRegistryAccess _registry = new();
    private readonly FakePrivilegeChecker _privileges = new();
    private readonly FakeFileSystemProbe _fs = new();
    private readonly AppPublisher _publisher;

    public AppPublisherTests()
    {
        _fs.Files.Add(Notepad);
        _fs.Files.Add(Calc);
        _publisher = new AppPublisher(_registry, _privileges, _fs, NullLogger<AppPublisher>.Instance);
    }

    private static string AppKey(string alias) => $@"{RegistryPaths.Applications}\{alias}";

    [Fact]
    public void Publish_creates_registry_entry_with_all_values()
    {
        var result = _publisher.Publish(new PublishRequest(@"%SystemRoot%\System32\notepad.exe", "Bloco de Notas"));

        Assert.Equal(PublishOutcome.Created, result.Outcome);
        Assert.Equal("bloco-de-notas", result.App.Alias);
        var key = AppKey("bloco-de-notas");
        Assert.Equal("Bloco de Notas", _registry.GetValue(key, "Name"));
        Assert.Equal(Notepad, _registry.GetValue(key, "Path"));
        Assert.Equal(@"%SystemRoot%\System32\notepad.exe", _registry.GetValue(key, "VPath"));
        Assert.Equal(Notepad, _registry.GetValue(key, "IconPath"));
        Assert.Equal(0, _registry.GetValue(key, "IconIndex"));
        Assert.Equal(0, _registry.GetValue(key, "CommandLineSetting"));
        Assert.Equal(1, _registry.GetValue(key, "ShowInTSWA"));
        Assert.Equal(1, _registry.GetValue(key, RegistryPaths.ManagedMarker));
        Assert.Equal(RegistryValueKind.DWord, _registry.GetKind(key, "IconIndex"));
        Assert.True(result.App.IsManaged);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Publish_twice_is_idempotent()
    {
        var request = new PublishRequest(Notepad, "Bloco de Notas");
        _publisher.Publish(request);
        var writes = _registry.WriteCount;

        var second = _publisher.Publish(request);

        Assert.Equal(PublishOutcome.Unchanged, second.Outcome);
        Assert.Equal(writes, _registry.WriteCount);
        Assert.Single(_publisher.List());
    }

    [Fact]
    public void Publish_same_exe_without_alias_reuses_existing_alias()
    {
        _publisher.Publish(new PublishRequest(Notepad, "Bloco de Notas"));

        var result = _publisher.Publish(new PublishRequest(Notepad, "Notepad"));

        Assert.Equal(PublishOutcome.Updated, result.Outcome);
        Assert.Equal("bloco-de-notas", result.App.Alias);
        Assert.Equal("Notepad", _registry.GetValue(AppKey("bloco-de-notas"), "Name"));
        Assert.Single(_publisher.List());
    }

    [Fact]
    public void Publish_generates_unique_alias_on_name_collision()
    {
        _publisher.Publish(new PublishRequest(Notepad, "Ferramenta"));

        var result = _publisher.Publish(new PublishRequest(Calc, "Ferramenta"));

        Assert.Equal("ferramenta-2", result.App.Alias);
        Assert.Equal(2, _publisher.List().Count);
    }

    [Fact]
    public void Publish_with_required_arguments_sets_policy()
    {
        var result = _publisher.Publish(new PublishRequest(Notepad, "Log", "log",
            CommandLinePolicy: CommandLinePolicy.Required, RequiredArguments: @"C:\log.txt"));

        Assert.Equal(2, _registry.GetValue(AppKey("log"), "CommandLineSetting"));
        Assert.Equal(@"C:\log.txt", _registry.GetValue(AppKey("log"), "RequiredCommandLine"));
        Assert.Equal(CommandLinePolicy.Required, result.App.CommandLinePolicy);
    }

    [Fact]
    public void Publish_warns_on_user_writable_location()
    {
        const string exe = @"C:\Users\joao\Downloads\tool.exe";
        _fs.Files.Add(exe);
        _fs.WritableDirs.Add(@"C:\Users\joao\Downloads");

        var result = _publisher.Publish(new PublishRequest(exe, "Tool"));

        Assert.Contains(PublishWarnings.WritableLocation, result.Warnings);
        Assert.Equal(PublishOutcome.Created, result.Outcome);
    }

    [Theory]
    [InlineData(@"C:\nao\existe.exe", "Nome", null, "ExecutablePath")]
    [InlineData(@"notepad.exe", "Nome", null, "ExecutablePath")]
    [InlineData(@"C:\Windows\System32\notepad.exe", " ", null, "DisplayName")]
    [InlineData(@"C:\Windows\System32\notepad.exe", "Nome", "com espaço", "Alias")]
    [InlineData(@"C:\Windows\System32\notepad.exe", "Nome", @"..\x", "Alias")]
    public void Publish_rejects_invalid_requests_without_writing(string path, string name, string? alias, string field)
    {
        var ex = Assert.Throws<AppValidationException>(() => _publisher.Publish(new PublishRequest(path, name, alias)));

        Assert.Equal(field, ex.Field);
        Assert.Equal(0, _registry.WriteCount);
    }

    [Fact]
    public void Publish_rejects_non_exe_files()
    {
        _fs.Files.Add(@"C:\docs\file.txt");

        var ex = Assert.Throws<AppValidationException>(() => _publisher.Publish(new PublishRequest(@"C:\docs\file.txt", "Doc")));

        Assert.Equal("ExecutablePath", ex.Field);
    }

    [Fact]
    public void Publish_rejects_required_policy_without_arguments()
    {
        var ex = Assert.Throws<AppValidationException>(() =>
            _publisher.Publish(new PublishRequest(Notepad, "X", CommandLinePolicy: CommandLinePolicy.Required)));

        Assert.Equal("RequiredArguments", ex.Field);
    }

    [Fact]
    public void Write_operations_require_admin()
    {
        _privileges.IsAdmin = false;

        Assert.Throws<AdminRequiredException>(() => _publisher.Publish(new PublishRequest(Notepad, "N")));
        Assert.Throws<AdminRequiredException>(() => _publisher.Unpublish("n"));
        Assert.Equal(0, _registry.WriteCount);
    }

    [Fact]
    public void List_does_not_require_admin_and_includes_unmanaged_apps()
    {
        _registry.Seed(AppKey("legacy"), "Name", "Legado");
        _registry.Seed(AppKey("legacy"), "Path", Calc);
        _registry.Seed(AppKey("legacy"), "CommandLineSetting", 1, RegistryValueKind.DWord);
        _privileges.IsAdmin = false;

        var app = Assert.Single(_publisher.List());

        Assert.Equal("legacy", app.Alias);
        Assert.Equal("Legado", app.DisplayName);
        Assert.Equal(CommandLinePolicy.Any, app.CommandLinePolicy);
        Assert.Equal(Calc, app.IconPath);
        Assert.False(app.IsManaged);
    }

    [Fact]
    public void Unpublish_removes_only_target_app()
    {
        _registry.Seed(AppKey("legacy"), "Name", "Legado");
        _registry.Seed(RegistryPaths.AllowList, "fDisabledAllowList", 0, RegistryValueKind.DWord);
        _publisher.Publish(new PublishRequest(Notepad, "Bloco de Notas"));

        Assert.True(_publisher.Unpublish("BLOCO-DE-NOTAS"));

        Assert.Null(_publisher.Get("bloco-de-notas"));
        Assert.NotNull(_publisher.Get("legacy"));
        Assert.True(_registry.KeyExists(RegistryPaths.Applications));
        Assert.Equal(0, _registry.GetValue(RegistryPaths.AllowList, "fDisabledAllowList"));
    }

    [Fact]
    public void Unpublish_missing_app_returns_false()
    {
        Assert.False(_publisher.Unpublish("nada"));
    }

    [Fact]
    public void Unpublish_rejects_invalid_alias_to_prevent_deleting_parent_keys()
    {
        _registry.Seed(AppKey("legacy"), "Name", "Legado");

        Assert.Throws<AppValidationException>(() => _publisher.Unpublish(@"..\..\Applications"));
        Assert.Throws<AppValidationException>(() => _publisher.Unpublish(""));
        Assert.NotNull(_publisher.Get("legacy"));
    }

    [Fact]
    public void Unpublish_twice_is_idempotent()
    {
        _publisher.Publish(new PublishRequest(Notepad, "N", "n"));

        Assert.True(_publisher.Unpublish("n"));
        Assert.False(_publisher.Unpublish("n"));
    }
}
