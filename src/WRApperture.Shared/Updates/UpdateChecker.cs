using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WRApperture.Shared.Updates;

public sealed record ReleaseAsset(string Name, Uri Url, long Size);

/// <param name="PageUrl">The release page on github.com (null if the API sent anything else).</param>
public sealed record ReleaseInfo(Version Version, string Tag, string? Notes, Uri? PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string name) => Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool IsNewerThan(Version current) => Version > current;
}

/// <summary>A downloaded setup whose SHA-256 matched the release's SHA256SUMS.txt.</summary>
public sealed record VerifiedSetup(string Path, string Sha256, Version Version);

/// <summary>The release did not pass verification (missing file or checksum, wrong checksum): never run it.</summary>
public sealed class UpdateVerificationException(string message) : Exception(message);

public interface IUpdateChecker
{
    /// <summary>The latest published release, or null when there is none (404, draft, pre-release, unknown tag).</summary>
    /// <exception cref="HttpRequestException">Network or GitHub failure.</exception>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>Downloads the product's setup into <paramref name="targetDirectory"/> and checks it against SHA256SUMS.txt.</summary>
    /// <exception cref="UpdateVerificationException">Fail closed: the file is deleted and must not be run.</exception>
    /// <exception cref="HttpRequestException">Network or GitHub failure.</exception>
    Task<VerifiedSetup> DownloadVerifiedAsync(ReleaseInfo release, UpdateProduct product, string targetDirectory, CancellationToken ct = default);
}

/// <summary>Reads the latest GitHub release of <see cref="UpdateSource.Repository"/> and downloads its installers.</summary>
public sealed class UpdateChecker : IUpdateChecker
{
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(20);

    /// <summary>Far above any real setup; stops a misbehaving server from filling the disk.</summary>
    private const long MaxSetupBytes = 1L << 30;
    private const long MaxChecksumsBytes = 64 * 1024;

    private readonly HttpClient _http;

    /// <param name="http">Used as is; requests carry their own headers and deadlines.</param>
    public UpdateChecker(HttpClient http) => _http = http;

    /// <summary>A checker with its own HttpClient (lives as long as the process).</summary>
    public static UpdateChecker CreateDefault() => new(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

    /// <summary>GitHub rejects API requests without a User-Agent.</summary>
    public static ProductInfoHeaderValue UserAgent { get; } = new("WRApperture", ProductVersion.Format(ProductVersion.Current));

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var deadline = Deadline(ApiTimeout, ct);
        using var request = Request(new Uri(UpdateSource.LatestReleaseApi), "application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // no release published yet
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        GitHubRelease? release;
        try
        {
            release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: deadline.Token);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("GitHub sent an unreadable release", ex);
        }

        return release is null ? null : ToRelease(release);
    }

    /// <summary>Maps the API answer, keeping only HTTPS github.com URLs of this repository.</summary>
    private static ReleaseInfo? ToRelease(GitHubRelease release)
    {
        if (release.Draft || release.PreRelease || ProductVersion.Parse(release.TagName) is not { } version)
        {
            return null;
        }

        var assets = (release.Assets ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && IsReleaseDownload(a.DownloadUrl))
            .Select(a => new ReleaseAsset(a.Name!, new Uri(a.DownloadUrl!), a.Size))
            .ToList();
        var page = Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps &&
                   url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? url
            : null;
        return new ReleaseInfo(version, release.TagName!, release.Body, page, assets);
    }

    private static bool IsReleaseDownload(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) &&
        url.Scheme == Uri.UriSchemeHttps &&
        url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        url.IsDefaultPort &&
        url.AbsolutePath.StartsWith(UpdateSource.DownloadPathPrefix, StringComparison.OrdinalIgnoreCase);

    public async Task<VerifiedSetup> DownloadVerifiedAsync(ReleaseInfo release, UpdateProduct product, string targetDirectory,
        CancellationToken ct = default)
    {
        var name = UpdateSource.SetupAssetName(product, release.Version);
        var setup = release.Asset(name) ?? throw new UpdateVerificationException($"The release {release.Tag} has no {name}");
        var sums = release.Asset(UpdateSource.ChecksumsAsset)
                   ?? throw new UpdateVerificationException($"The release {release.Tag} has no {UpdateSource.ChecksumsAsset}");

        using var deadline = Deadline(DownloadTimeout, ct);
        var expected = ParseChecksums(await DownloadTextAsync(sums.Url, deadline.Token)).GetValueOrDefault(name)
                       ?? throw new UpdateVerificationException($"{UpdateSource.ChecksumsAsset} of {release.Tag} has no entry for {name}");

        Directory.CreateDirectory(targetDirectory);
        var path = System.IO.Path.Combine(targetDirectory, name);
        var partial = path + ".partial";
        try
        {
            var actual = await DownloadFileAsync(setup.Url, partial, deadline.Token);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected)))
            {
                throw new UpdateVerificationException($"{name} does not match its SHA-256 in {UpdateSource.ChecksumsAsset} (got {actual}, expected {expected})");
            }

            File.Move(partial, path, overwrite: true);
            return new VerifiedSetup(path, expected, release.Version);
        }
        finally
        {
            File.Delete(partial);
        }
    }

    /// <summary>"&lt;hex&gt;  &lt;name&gt;" (or "&lt;hex&gt; *&lt;name&gt;", binary mode) per line → name → lowercase hex.</summary>
    public static IReadOnlyDictionary<string, string> ParseChecksums(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space != 64)
            {
                continue;
            }

            var hash = line[..space];
            var file = line[space..].Trim().TrimStart('*');
            if (file.Length > 0 && hash.All(Uri.IsHexDigit))
            {
                sums[file] = hash.ToLowerInvariant();
            }
        }

        return sums;
    }

    /// <summary>SHA-256 of a file as lowercase hex.</summary>
    public static string Sha256Of(Stream stream) => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    private async Task<string> DownloadTextAsync(Uri url, CancellationToken ct)
    {
        using var request = Request(url, "application/octet-stream");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxChecksumsBytes)
        {
            throw new UpdateVerificationException($"{UpdateSource.ChecksumsAsset} is too large");
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Streams to <paramref name="path"/> while hashing; returns the SHA-256 as lowercase hex.</summary>
    private async Task<string> DownloadFileAsync(Uri url, string path, CancellationToken ct)
    {
        using var request = Request(url, "application/octet-stream");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxSetupBytes)
            {
                throw new UpdateVerificationException($"{url} is larger than {MaxSetupBytes} bytes");
            }

            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static HttpRequestMessage Request(Uri url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return request;
    }

    private static CancellationTokenSource Deadline(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        return deadline;
    }

    /// <summary>The part of GitHub's "release" object that matters here.</summary>
    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; init; }
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
        [JsonPropertyName("draft")] public bool Draft { get; init; }
        [JsonPropertyName("prerelease")] public bool PreRelease { get; init; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; init; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("size")] public long Size { get; init; }
        [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; init; }
    }
}
