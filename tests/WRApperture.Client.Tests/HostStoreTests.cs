using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Storage;

namespace WRApperture.Client.Tests;

public class HostStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly HostStore _store;

    public HostStoreTests() => _store = new HostStore(_dir.Paths);

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Empty_state_when_no_file()
    {
        Assert.Empty(_store.Load().Hosts);
    }

    [Fact]
    public void Round_trips_state()
    {
        var host = Samples.Host(user: "joao");
        _store.Save(new ClientState { Hosts = [host], SelectedHostId = host.Id });

        var loaded = _store.Load();

        var h = Assert.Single(loaded.Hosts);
        Assert.Equal(host.Id, loaded.SelectedHostId);
        Assert.Equal("192.168.0.10", h.Address);
        Assert.Equal("joao", h.RdpUserName);
        Assert.Equal(["bloco-de-notas", "calc"], h.Apps.Select(a => a.Alias));
        Assert.Equal("secret-token", HostStore.UnprotectToken(h.ProtectedToken));
    }

    [Fact]
    public void Token_never_appears_in_clear_text_on_disk()
    {
        _store.Save(new ClientState { Hosts = [Samples.Host()] });

        Assert.DoesNotContain("secret-token", File.ReadAllText(_dir.Paths.StateFile));
    }

    [Fact]
    public void Save_leaves_no_temp_file()
    {
        _store.Save(new ClientState { Hosts = [Samples.Host()] });
        _store.Save(new ClientState());

        Assert.False(File.Exists(_dir.Paths.StateFile + ".tmp"));
        Assert.Empty(_store.Load().Hosts);
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_yields_empty_state()
    {
        Directory.CreateDirectory(_dir.Path);
        File.WriteAllText(_dir.Paths.StateFile, "{ not json");

        Assert.Empty(_store.Load().Hosts);
        Assert.Single(Directory.GetFiles(_dir.Path, "hosts.json.corrupt-*"));
    }

    [Fact]
    public void State_from_v030_loads_with_release_without_asking()
    {
        Directory.CreateDirectory(_dir.Path);
        File.WriteAllText(_dir.Paths.StateFile, """
            {"version":1,"hosts":[{"id":"11111111-2222-3333-4444-555555555555","displayName":"PC","address":"10.0.0.2",
             "agentPort":5080,"protectedToken":"x","pinnedFingerprint":"AB","autoReleaseSession":false}]}
            """);

        var host = Assert.Single(_store.Load().Hosts);

        Assert.False(host.AskBeforeReleasingSession);
    }

    [Fact]
    public void Protected_token_is_bound_to_entropy()
    {
        var protectedToken = HostStore.ProtectToken("abc");

        Assert.NotEqual("abc", protectedToken);
        Assert.Equal("abc", HostStore.UnprotectToken(protectedToken));
    }

    [Theory]
    [InlineData("notepad", "notepad")]
    [InlineData("../evil", "___evil")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("", "app")]
    public void Safe_file_names(string alias, string expected)
    {
        Assert.Equal(expected, ClientPaths.SafeFileName(alias));
    }
}
