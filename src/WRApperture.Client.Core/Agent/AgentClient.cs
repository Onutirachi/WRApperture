using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Storage;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Core.Agent;

public sealed record PairingResult(HostInfoDto Host, string ObservedFingerprint);

public sealed record IconResult(bool NotModified, byte[]? Png, string? ETag);

/// <summary>Read operations of the agent API (seam for tests).</summary>
public interface IAgentApi : IDisposable
{
    Task<HostInfoDto> GetHostInfoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AppSummaryDto>> GetAppsAsync(CancellationToken ct = default);
    Task<IconResult> GetIconAsync(string alias, int size, string? etag, CancellationToken ct = default);

    /// <summary>
    /// Null when the agent predates session support (HTTP 404). <paramref name="desktop"/> asks for the
    /// full desktop rule; only send it to agents that announce <see cref="AgentFeatures.DesktopSession"/>.
    /// </summary>
    Task<SessionStatusDto?> GetSessionStatusAsync(string? rdpUser, bool desktop = false, CancellationToken ct = default);

    /// <summary>Frees the host sessions that block the connection; waits up to 45 s.</summary>
    Task<SessionReleaseResultDto> ReleaseSessionAsync(string? rdpUser, bool desktop = false, CancellationToken ct = default);

    /// <summary>
    /// Asks the agent to install the latest release (it downloads it from GitHub itself; nothing is sent).
    /// Only for agents that announce <see cref="AgentFeatures.SelfUpdate"/>.
    /// </summary>
    Task<UpdateRequestResultDto> RequestHostUpdateAsync(CancellationToken ct = default);
}

/// <summary>Talks to a host agent over HTTPS with certificate pinning.</summary>
public sealed class AgentClient : IAgentApi
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly CertificatePinValidator _validator;
    private readonly string _address;
    private readonly int _port;

    internal AgentClient(HttpClient http, CertificatePinValidator validator, string address, int port)
    {
        _http = http;
        _validator = validator;
        _address = address;
        _port = port;
    }

    public static AgentClient ForHost(PairedHost host)
    {
        var validator = CertificatePinValidator.Pinned(host.PinnedFingerprint);
        return new AgentClient(CreateHttp(host.Address, host.AgentPort, ReadToken(host), validator),
            validator, host.Address, host.AgentPort);
    }

    /// <summary>DPAPI data only decrypts for the user/machine that created it (e.g. a copied hosts.json fails).</summary>
    private static string ReadToken(PairedHost host)
    {
        try
        {
            return HostStore.UnprotectToken(host.ProtectedToken);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            throw new InvalidTokenException(Strings.T("client.errors.tokenUnreadable"), ex);
        }
    }

    /// <summary>
    /// First contact: accepts the presented certificate but requires it to match the fingerprint the
    /// agent reports about itself (a TLS-terminating interceptor would present a different one).
    /// </summary>
    public static async Task<PairingResult> PairAsync(string address, int agentPort, string token, CancellationToken ct = default)
    {
        var validator = CertificatePinValidator.ForPairing();
        using var client = new AgentClient(CreateHttp(address, agentPort, token, validator), validator, address, agentPort);
        return await client.CompletePairingAsync(ct);
    }

    internal async Task<PairingResult> CompletePairingAsync(CancellationToken ct)
    {
        var info = await GetHostInfoAsync(ct);
        var observed = _validator.Observed;
        if (observed is null || !string.Equals(observed, info.CertificateFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new PairingMismatchException();
        }

        return new PairingResult(info, observed);
    }

    public async Task<HostInfoDto> GetHostInfoAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, ApiContract.Host), ct);
        return await response.Content.ReadFromJsonAsync<HostInfoDto>(ct)
               ?? throw new AgentException(Strings.T("client.errors.invalidResponse"));
    }

    public async Task<IReadOnlyList<AppSummaryDto>> GetAppsAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, ApiContract.Apps), ct);
        return await response.Content.ReadFromJsonAsync<List<AppSummaryDto>>(ct) ?? [];
    }

    public async Task<IconResult> GetIconAsync(string alias, int size, string? etag, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiContract.Icon(alias)}?size={size}");
        if (etag is not null && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        using var response = await SendAsync(request, ct, allowNotModified: true);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new IconResult(true, null, etag);
        }

        return new IconResult(false, await response.Content.ReadAsByteArrayAsync(ct), response.Headers.ETag?.ToString());
    }

    public async Task<SessionStatusDto?> GetSessionStatusAsync(string? rdpUser, bool desktop = false, CancellationToken ct = default)
    {
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(rdpUser))
        {
            parameters.Add("user=" + Uri.EscapeDataString(rdpUser.Trim()));
        }

        if (desktop)
        {
            parameters.Add("mode=" + SessionModes.Desktop);
        }

        var query = parameters.Count == 0 ? string.Empty : "?" + string.Join('&', parameters);
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, ApiContract.Session + query), ct, allowNotFound: true);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await response.Content.ReadFromJsonAsync<SessionStatusDto>(ct);
    }

    public async Task<SessionReleaseResultDto> ReleaseSessionAsync(string? rdpUser, bool desktop = false, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiContract.SessionRelease)
        {
            Content = JsonContent.Create(new SessionReleaseRequestDto(
                string.IsNullOrWhiteSpace(rdpUser) ? null : rdpUser.Trim(),
                desktop ? SessionModes.Desktop : null)),
        };
        using var response = await SendAsync(request, ct, timeout: TimeSpan.FromSeconds(ApiContract.SessionReleaseTimeoutSeconds));
        return await response.Content.ReadFromJsonAsync<SessionReleaseResultDto>(ct)
               ?? throw new AgentException(Strings.T("client.errors.invalidResponse"));
    }

    public async Task<UpdateRequestResultDto> RequestHostUpdateAsync(CancellationToken ct = default)
    {
        // The agent asks GitHub before answering (up to 30 s); the download itself happens after the answer.
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, ApiContract.Update), ct, timeout: TimeSpan.FromSeconds(40));
        return await response.Content.ReadFromJsonAsync<UpdateRequestResultDto>(ct)
               ?? throw new AgentException(Strings.T("client.errors.invalidResponse"));
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct,
        bool allowNotModified = false, bool allowNotFound = false, TimeSpan? timeout = null)
    {
        HttpResponseMessage response;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? RequestTimeout);
        try
        {
            response = await _http.SendAsync(request, deadline.Token);
        }
        catch (HttpRequestException ex)
        {
            if (_validator.Mismatch is { } observed)
            {
                throw new CertificateChangedException(observed);
            }

            throw new AgentUnreachableException(_address, _port, ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AgentUnreachableException(_address, _port, ex);
        }
        finally
        {
            request.Dispose();
        }

        if (response.IsSuccessStatusCode ||
            (allowNotModified && response.StatusCode == HttpStatusCode.NotModified) ||
            (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
        {
            return response;
        }

        using (response)
        {
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new InvalidTokenException(),
                HttpStatusCode.TooManyRequests => new TooManyAttemptsException(),
                _ => new AgentException(await ReadErrorAsync(response, ct)),
            };
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(ct);
            if (!string.IsNullOrWhiteSpace(error?.Message))
            {
                return error.Message;
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
        }

        return Strings.T("client.errors.httpError", (int)response.StatusCode);
    }

    private static HttpClient CreateHttp(string address, int port, string token, CertificatePinValidator validator)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = RequestTimeout,
            // Force a fresh TLS handshake per client so the pin is always evaluated.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) => validator.Validate(certificate);

        var http = new HttpClient(handler)
        {
            BaseAddress = new UriBuilder(Uri.UriSchemeHttps, address.Trim(), port).Uri,
            Timeout = Timeout.InfiniteTimeSpan, // per-request deadlines in SendAsync (release can take 45 s)
        };
        http.DefaultRequestHeaders.Add(ApiContract.TokenHeader, token);
        return http;
    }
}
