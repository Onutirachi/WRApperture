using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Storage;
using WRApperture.Shared;

namespace WRApperture.Client.Core.Agent;

public sealed record PairingResult(HostInfoDto Host, string ObservedFingerprint);

public sealed record IconResult(bool NotModified, byte[]? Png, string? ETag);

/// <summary>Talks to a host agent over HTTPS with certificate pinning.</summary>
public sealed class AgentClient : IDisposable
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
        return new AgentClient(CreateHttp(host.Address, host.AgentPort, HostStore.UnprotectToken(host.ProtectedToken), validator),
            validator, host.Address, host.AgentPort);
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
               ?? throw new AgentException("Resposta inválida do host.");
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

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct, bool allowNotModified = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            if (_validator.Mismatch is { } observed)
            {
                throw new CertificateChangedException(observed);
            }

            throw new AgentUnreachableException(_address, _port, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AgentUnreachableException(_address, _port, ex);
        }
        finally
        {
            request.Dispose();
        }

        if (response.IsSuccessStatusCode || (allowNotModified && response.StatusCode == HttpStatusCode.NotModified))
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

        return $"O host respondeu com erro {(int)response.StatusCode}.";
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
            Timeout = RequestTimeout,
        };
        http.DefaultRequestHeaders.Add(ApiContract.TokenHeader, token);
        return http;
    }
}
