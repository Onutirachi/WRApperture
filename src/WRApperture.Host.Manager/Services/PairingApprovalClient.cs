using System.Net.Http;
using System.Net.Http.Json;
using WRApperture.Shared;

namespace WRApperture.Host.Manager.Services;

/// <summary>
/// Talks to the agent on this PC (https://localhost) for the approval window it opened. The agent's certificate
/// must match the one registered in its settings, so the decision only ever reaches the real agent.
/// </summary>
internal sealed class PairingApprovalClient : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _http;

    public PairingApprovalClient(int port, string? expectedFingerprint, string secret)
    {
        var expected = expectedFingerprint?.Replace(" ", string.Empty, StringComparison.Ordinal);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && expected is not null &&
                string.Equals(CertificateFingerprint.Compute(certificate), expected, StringComparison.OrdinalIgnoreCase),
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{port}"), Timeout = Timeout };
        _http.DefaultRequestHeaders.Add(ApiContract.ApprovalHeader, secret);
    }

    /// <summary>Null when the request no longer exists or is no longer pending.</summary>
    public async Task<PairingDetailsDto?> GetDetailsAsync(string id)
    {
        using var response = await _http.GetAsync(ApiContract.PairingApproval(id)).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PairingDetailsDto>().ConfigureAwait(false) : null;
    }

    /// <summary>Never resumes on the caller's context, so the window can wait for it while closing.</summary>
    public async Task<bool> DecideAsync(string id, bool approve)
    {
        using var response = await _http.PostAsJsonAsync(ApiContract.PairingApproval(id), new PairingDecisionDto(approve)).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public void Dispose() => _http.Dispose();
}
