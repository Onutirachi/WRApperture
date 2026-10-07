using WRApperture.Client.Core;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Credentials;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Shortcuts;
using WRApperture.Client.Core.Storage;
using WRApperture.Shared;

namespace WRApperture.Client.Tests;

public class HostSyncTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeAgent _agent = new();
    private readonly IconStore _icons;
    private readonly HostSync _sync;

    public HostSyncTests()
    {
        _icons = new IconStore(_dir.Paths);
        _sync = new HostSync(_icons, _ => _agent, TimeProvider.System);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Updates_apps_rdp_port_and_icons()
    {
        var host = Samples.Host() with { Apps = [] };

        var synced = await _sync.SyncAsync(host);

        Assert.Equal(["notepad", "calc"], synced.Apps.Select(a => a.Alias));
        Assert.Equal(3390, synced.RdpPort);
        Assert.NotNull(synced.LastSyncUtc);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(_icons.PngPath(host.Id, "notepad")));
        Assert.Equal("\"e-notepad\"", synced.Apps[0].IconETag);
        Assert.All(_agent.IconSizes, s => Assert.Equal(HostSync.IconSize, s));
    }

    [Fact]
    public async Task Sends_cached_etag_only_when_icon_file_exists()
    {
        var host = Samples.Host() with { Apps = [new CachedApp("notepad", "N", "\"old\""), new CachedApp("calc", "C", "\"old-calc\"")] };
        _icons.Write(host.Id, "notepad", [9]);

        await _sync.SyncAsync(host);

        Assert.Equal("\"old\"", _agent.SentETags["notepad"]);
        Assert.Null(_agent.SentETags["calc"]);
    }

    [Fact]
    public async Task Not_modified_keeps_existing_icon()
    {
        var host = Samples.Host() with { Apps = [new CachedApp("notepad", "N", "\"old\"")] };
        _icons.Write(host.Id, "notepad", [9]);
        _agent.NotModified = true;

        var synced = await _sync.SyncAsync(host);

        Assert.Equal([9], File.ReadAllBytes(_icons.PngPath(host.Id, "notepad")));
        Assert.Equal("\"old\"", synced.Apps.Single(a => a.Alias == "notepad").IconETag);
    }

    [Fact]
    public async Task Icon_failure_does_not_drop_the_app()
    {
        _agent.FailIconFor = "calc";

        var synced = await _sync.SyncAsync(Samples.Host());

        Assert.Contains(synced.Apps, a => a.Alias == "calc");
    }

    [Fact]
    public async Task Keeps_local_names_of_apps_still_published()
    {
        var host = Samples.Host() with
        {
            Apps = [new CachedApp("notepad", "N", CustomName: "Notas"), new CachedApp("gone", "G", CustomName: "Sumiu")],
        };

        var synced = await _sync.SyncAsync(host);

        Assert.Equal("Notas", synced.Apps.Single(a => a.Alias == "notepad").Name);
        Assert.Null(synced.Apps.Single(a => a.Alias == "calc").CustomName);
        Assert.DoesNotContain(synced.Apps, a => a.Alias == "gone");
    }

    [Fact]
    public async Task Auth_errors_propagate()
    {
        _agent.ThrowOnApps = new InvalidTokenException();

        await Assert.ThrowsAsync<InvalidTokenException>(() => _sync.SyncAsync(Samples.Host()));
    }

    private sealed class FakeAgent : IAgentApi
    {
        public bool NotModified { get; set; }
        public string? FailIconFor { get; set; }
        public Exception? ThrowOnApps { get; set; }
        public Dictionary<string, string?> SentETags { get; } = [];
        public List<int> IconSizes { get; } = [];

        public Task<HostInfoDto> GetHostInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new HostInfoDto("PC", 3390, "0.1.0", "FP"));

        public Task<IReadOnlyList<AppSummaryDto>> GetAppsAsync(CancellationToken ct = default) =>
            ThrowOnApps is not null
                ? Task.FromException<IReadOnlyList<AppSummaryDto>>(ThrowOnApps)
                : Task.FromResult<IReadOnlyList<AppSummaryDto>>(
                [
                    new("notepad", "Bloco de Notas", ApiContract.Icon("notepad")),
                    new("calc", "Calculadora", ApiContract.Icon("calc")),
                ]);

        public Task<IconResult> GetIconAsync(string alias, int size, string? etag, CancellationToken ct = default)
        {
            SentETags[alias] = etag;
            IconSizes.Add(size);
            if (alias == FailIconFor)
            {
                throw new AgentException("boom");
            }

            return Task.FromResult(NotModified ? new IconResult(true, null, etag) : new IconResult(false, [1, 2, 3], $"\"e-{alias}\""));
        }

        public Task<SessionStatusDto?> GetSessionStatusAsync(string? rdpUser, CancellationToken ct = default) =>
            Task.FromResult<SessionStatusDto?>(null);

        public Task<SessionReleaseResultDto> ReleaseSessionAsync(string? rdpUser, CancellationToken ct = default) =>
            Task.FromResult(new SessionReleaseResultDto(0, false));

        public void Dispose()
        {
        }
    }
}

[Trait("Category", "Integration")]
public class WindowsCredentialStoreTests
{
    [Fact]
    public void Save_exists_delete_round_trip_on_a_test_target()
    {
        // Domain-password credentials need a host-like target; use a unique, non-resolvable name.
        var store = new WindowsCredentialStore();
        var address = $"wrapperture-unit-{Guid.NewGuid():N}.invalid";
        try
        {
            store.Save(address, "user", "p@ss");
            Assert.True(store.Exists(address));
        }
        finally
        {
            store.Delete(address);
        }

        Assert.False(store.Exists(address));
        store.Delete(address); // idempotent
    }

    [Fact]
    public void Default_target_is_termsrv()
    {
        Assert.Equal("TERMSRV/192.168.0.10", new WindowsCredentialStore().Target(" 192.168.0.10 "));
    }
}

public class ShortcutTests : IDisposable
{
    private static readonly byte[] Png256 = MakePngHeader(256, 256);
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Ico_wraps_png_with_correct_header()
    {
        var ico = IcoWriter.FromPng(Png256);

        Assert.Equal([0, 0, 1, 0, 1, 0], ico[..6]);
        Assert.Equal(0, ico[6]); // 256 => 0
        Assert.Equal(0, ico[7]);
        Assert.Equal((uint)Png256.Length, BitConverter.ToUInt32(ico, 14));
        Assert.Equal(22u, BitConverter.ToUInt32(ico, 18));
        Assert.Equal(Png256, ico[22..]);
    }

    [Fact]
    public void Ico_keeps_small_sizes()
    {
        var ico = IcoWriter.FromPng(MakePngHeader(48, 48));

        Assert.Equal(48, ico[6]);
    }

    [Fact]
    public void Ico_rejects_non_png()
    {
        Assert.Throws<ArgumentException>(() => IcoWriter.FromPng(new byte[30]));
    }

    [Theory]
    [InlineData("Bloco de Notas", "Bloco de Notas")]
    [InlineData("A/B:C*?", "A-B-C--")]
    [InlineData("  ", "WRApperture")]
    public void Safe_shortcut_names(string name, string expected)
    {
        Assert.Equal(expected, DesktopShortcuts.SafeName(name));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Creates_lnk_in_directory()
    {
        var rdp = Path.Combine(_dir.Path, "a.rdp");
        var ico = Path.Combine(_dir.Path, "a.ico");
        File.WriteAllText(rdp, "x");
        File.WriteAllBytes(ico, IcoWriter.FromPng(Png256));

        var lnk = DesktopShortcuts.Create("Ação: teste", rdp, ico, _dir.Path);

        Assert.True(File.Exists(lnk));
        Assert.EndsWith("Ação- teste.lnk", lnk);
    }

    private static byte[] MakePngHeader(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }
}
