using System.Net;
using System.Security.Cryptography;
using System.Text;
using WRApperture.Shared.Updates;

namespace WRApperture.Client.Tests;

/// <summary>GitHub is a fake HttpMessageHandler: nothing here touches the network.</summary>
public sealed class UpdateCheckerTests : IDisposable
{
    private const string Download = "https://github.com/Onutirachi/WRApperture/releases/download/v1.2.3/";
    private static readonly byte[] ClientSetup = Encoding.ASCII.GetBytes("MZ fake client setup");
    private static readonly byte[] HostSetup = Encoding.ASCII.GetBytes("MZ fake host setup");

    private readonly TempDir _dir = new();
    private readonly FakeGitHub _github = new();
    private readonly UpdateChecker _checker;

    public UpdateCheckerTests()
    {
        _checker = new UpdateChecker(new HttpClient(_github));
        _github.Routes[UpdateSource.LatestReleaseApi] = Ok(Encoding.UTF8.GetBytes(ReleaseJson("v1.2.3")));
        _github.Routes[Download + "WRApperture-Client-Setup-1.2.3.exe"] = Ok(ClientSetup);
        _github.Routes[Download + "WRApperture-Host-Setup-1.2.3.exe"] = Ok(HostSetup);
        _github.Routes[Download + "SHA256SUMS.txt"] = Ok(Encoding.ASCII.GetBytes(
            $"{Sha(ClientSetup)}  WRApperture-Client-Setup-1.2.3.exe\n{Sha(HostSetup)}  WRApperture-Host-Setup-1.2.3.exe\n"));
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Parses_the_latest_release()
    {
        var release = await _checker.GetLatestAsync();

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 3), release.Version);
        Assert.Equal("v1.2.3", release.Tag);
        Assert.Contains("Automatic updates", release.Notes);
        Assert.Equal(new Uri("https://github.com/Onutirachi/WRApperture/releases/tag/v1.2.3"), release.PageUrl);
        Assert.Equal(["WRApperture-Client-Setup-1.2.3.exe", "WRApperture-Host-Setup-1.2.3.exe", "SHA256SUMS.txt"],
            release.Assets.Select(a => a.Name));
        Assert.Equal(48_123_456, release.Asset("WRApperture-Client-Setup-1.2.3.exe")!.Size);
    }

    [Fact]
    public async Task Sends_a_user_agent_and_asks_for_github_json()
    {
        await _checker.GetLatestAsync();

        var request = Assert.Single(_github.Requests);
        Assert.Equal("WRApperture", request.Headers.UserAgent.Single().Product!.Name);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
    }

    [Fact]
    public async Task No_release_yet_is_null()
    {
        _github.Routes[UpdateSource.LatestReleaseApi] = () => new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.Null(await _checker.GetLatestAsync());
    }

    [Fact]
    public async Task Server_errors_throw()
    {
        _github.Routes[UpdateSource.LatestReleaseApi] = () => new HttpResponseMessage(HttpStatusCode.Forbidden); // rate limited

        await Assert.ThrowsAsync<HttpRequestException>(() => _checker.GetLatestAsync());
    }

    [Theory]
    [InlineData("v1.3.0-beta.1")]
    [InlineData("nightly")]
    public async Task Pre_release_or_unknown_tags_are_ignored(string tag)
    {
        _github.Routes[UpdateSource.LatestReleaseApi] = Ok(Encoding.UTF8.GetBytes(ReleaseJson(tag)));

        Assert.Null(await _checker.GetLatestAsync());
    }

    [Fact]
    public async Task Only_https_downloads_of_this_repository_are_kept()
    {
        var json = ReleaseJson("v1.2.3")
            .Replace(Download + "WRApperture-Host-Setup-1.2.3.exe", "http://github.com/Onutirachi/WRApperture/releases/download/v1.2.3/WRApperture-Host-Setup-1.2.3.exe")
            .Replace(Download + "SHA256SUMS.txt", "https://evil.example/Onutirachi/WRApperture/releases/download/v1.2.3/SHA256SUMS.txt");
        _github.Routes[UpdateSource.LatestReleaseApi] = Ok(Encoding.UTF8.GetBytes(json));

        var release = await _checker.GetLatestAsync();

        Assert.Equal(["WRApperture-Client-Setup-1.2.3.exe"], release!.Assets.Select(a => a.Name));
    }

    [Theory]
    [InlineData(UpdateProduct.Client)]
    [InlineData(UpdateProduct.Host)]
    public async Task Downloads_and_verifies_the_setup(UpdateProduct product)
    {
        var release = (await _checker.GetLatestAsync())!;

        var setup = await _checker.DownloadVerifiedAsync(release, product, _dir.Path);

        var expected = product == UpdateProduct.Client ? ClientSetup : HostSetup;
        Assert.Equal(Path.Combine(_dir.Path, $"WRApperture-{product}-Setup-1.2.3.exe"), setup.Path);
        Assert.Equal(expected, File.ReadAllBytes(setup.Path));
        Assert.Equal(Sha(expected), setup.Sha256);
        Assert.Single(Directory.GetFiles(_dir.Path)); // no leftover .partial
    }

    [Fact]
    public async Task Checksum_mismatch_fails_closed_and_leaves_nothing_to_run()
    {
        _github.Routes[Download + "WRApperture-Client-Setup-1.2.3.exe"] = Ok(Encoding.ASCII.GetBytes("MZ tampered"));
        var release = (await _checker.GetLatestAsync())!;

        await Assert.ThrowsAsync<UpdateVerificationException>(() => _checker.DownloadVerifiedAsync(release, UpdateProduct.Client, _dir.Path));

        Assert.Empty(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public async Task Missing_checksum_entry_fails_closed_before_downloading_the_setup()
    {
        _github.Routes[Download + "SHA256SUMS.txt"] = Ok(Encoding.ASCII.GetBytes($"{Sha(HostSetup)}  WRApperture-Host-Setup-1.2.3.exe\n"));
        var release = (await _checker.GetLatestAsync())!;

        await Assert.ThrowsAsync<UpdateVerificationException>(() => _checker.DownloadVerifiedAsync(release, UpdateProduct.Client, _dir.Path));

        Assert.DoesNotContain(_github.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("Client-Setup-1.2.3.exe"));
        Assert.Empty(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public async Task Missing_checksums_file_fails_closed()
    {
        var release = (await _checker.GetLatestAsync())!;
        release = release with { Assets = release.Assets.Where(a => a.Name != UpdateSource.ChecksumsAsset).ToList() };

        await Assert.ThrowsAsync<UpdateVerificationException>(() => _checker.DownloadVerifiedAsync(release, UpdateProduct.Host, _dir.Path));
    }

    [Fact]
    public async Task Launcher_refuses_a_file_changed_after_verification()
    {
        var setup = await _checker.DownloadVerifiedAsync((await _checker.GetLatestAsync())!, UpdateProduct.Client, _dir.Path);
        File.WriteAllText(setup.Path, "swapped");

        Assert.Throws<UpdateVerificationException>(() => SetupLauncher.Start(setup, UpdateSource.SilentArguments));
    }

    [Fact]
    public async Task Launcher_starts_the_verified_file_while_holding_it()
    {
        // A copy of cmd.exe stands in for a setup: proves Windows runs a file we keep open denying writes.
        var path = Path.Combine(_dir.Path, "setup.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), path);
        string hash;
        using (var stream = File.OpenRead(path))
        {
            hash = UpdateChecker.Sha256Of(stream);
        }

        using var process = SetupLauncher.Start(new VerifiedSetup(path, hash, new Version(1, 2, 3)), "/c exit 3");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(3, process.ExitCode);
    }

    [Fact]
    public void Checksum_file_accepts_text_and_binary_mode_lines()
    {
        var hash = new string('a', 64);
        var sums = UpdateChecker.ParseChecksums($"{hash.ToUpperInvariant()}  one.exe\r\n{hash} *two.exe\r\n\r\nnot a line\r\n");

        Assert.Equal(hash, sums["one.exe"]);
        Assert.Equal(hash, sums["two.exe"]);
        Assert.Equal(2, sums.Count);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V0.15.0", "0.15.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.3+8f2c1ab", "1.2.3")]
    [InlineData("1.3.0-beta.1", null)]
    [InlineData("latest", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Parses_tags(string? tag, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), ProductVersion.Parse(tag));

    [Theory]
    [InlineData("0.14.1", "0.15.0", true)]
    [InlineData("0.15.0", "0.15.0", false)]
    [InlineData("0.15.0.0", "0.15.0", false)] // System.Version alone would call 0.15.0.0 newer
    [InlineData("0.9.0", "0.10.0", true)] // numeric, not text
    [InlineData("1.0.0+abc", "1.0.1", true)]
    [InlineData("garbage", "1.0.0", false)] // unknown is never "outdated"
    [InlineData(null, "1.0.0", false)]
    public void Compares_versions(string? candidate, string latest, bool older) =>
        Assert.Equal(older, ProductVersion.IsOlder(candidate, Version.Parse(latest)));

    [Fact]
    public void Current_version_is_this_build()
    {
        Assert.True(ProductVersion.Current > new Version(0, 14, 0));
        Assert.Equal(-1, ProductVersion.Current.Revision);
    }

    [Fact]
    public void Asset_names_follow_the_release_contract()
    {
        Assert.Equal("WRApperture-Client-Setup-1.2.3.exe", UpdateSource.SetupAssetName(UpdateProduct.Client, new Version(1, 2, 3)));
        Assert.Equal("WRApperture-Host-Setup-0.15.0.exe", UpdateSource.SetupAssetName(UpdateProduct.Host, new Version(0, 15, 0)));
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static Func<HttpResponseMessage> Ok(byte[] body) => () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    /// <summary>Trimmed from a real GET /repos/{owner}/{repo}/releases/latest answer.</summary>
    private static string ReleaseJson(string tag) => $$"""
        {
          "url": "https://api.github.com/repos/Onutirachi/WRApperture/releases/251234567",
          "assets_url": "https://api.github.com/repos/Onutirachi/WRApperture/releases/251234567/assets",
          "html_url": "https://github.com/Onutirachi/WRApperture/releases/tag/{{tag}}",
          "id": 251234567,
          "author": { "login": "github-actions[bot]", "id": 41898282, "type": "Bot", "site_admin": false },
          "node_id": "RE_kwDOOabc123",
          "tag_name": "{{tag}}",
          "target_commitish": "main",
          "name": "WRApperture {{tag}}",
          "draft": false,
          "immutable": false,
          "prerelease": false,
          "created_at": "2026-10-07T18:02:11Z",
          "published_at": "2026-10-07T18:09:45Z",
          "assets": [
            {
              "url": "https://api.github.com/repos/Onutirachi/WRApperture/releases/assets/298765431",
              "id": 298765431,
              "node_id": "RA_kwDOOabc123",
              "name": "WRApperture-Client-Setup-1.2.3.exe",
              "label": "",
              "uploader": { "login": "github-actions[bot]", "id": 41898282, "type": "Bot" },
              "content_type": "application/x-msdownload",
              "state": "uploaded",
              "size": 48123456,
              "digest": "sha256:0000000000000000000000000000000000000000000000000000000000000000",
              "download_count": 3,
              "created_at": "2026-10-07T18:09:40Z",
              "updated_at": "2026-10-07T18:09:42Z",
              "browser_download_url": "{{Download}}WRApperture-Client-Setup-1.2.3.exe"
            },
            {
              "url": "https://api.github.com/repos/Onutirachi/WRApperture/releases/assets/298765432",
              "id": 298765432,
              "name": "WRApperture-Host-Setup-1.2.3.exe",
              "content_type": "application/x-msdownload",
              "state": "uploaded",
              "size": 51234567,
              "download_count": 1,
              "browser_download_url": "{{Download}}WRApperture-Host-Setup-1.2.3.exe"
            },
            {
              "url": "https://api.github.com/repos/Onutirachi/WRApperture/releases/assets/298765433",
              "id": 298765433,
              "name": "SHA256SUMS.txt",
              "content_type": "text/plain",
              "state": "uploaded",
              "size": 200,
              "download_count": 4,
              "browser_download_url": "{{Download}}SHA256SUMS.txt"
            }
          ],
          "tarball_url": "https://api.github.com/repos/Onutirachi/WRApperture/tarball/{{tag}}",
          "zipball_url": "https://api.github.com/repos/Onutirachi/WRApperture/zipball/{{tag}}",
          "body": "## What's new\r\n\r\n- Automatic updates for the host and the client.\r\n"
        }
        """;

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.Ordinal);
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var route)
                ? route()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
