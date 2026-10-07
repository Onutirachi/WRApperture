using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Storage;
using WRApperture.Shared;

namespace WRApperture.Client.Tests;

/// <summary>Pinning over real TLS against a minimal loopback agent with a self-signed certificate.</summary>
public sealed class AgentClientTlsTests : IAsyncLifetime
{
    private const string Token = "tok-123";
    private readonly X509Certificate2 _certificate = CreateCertificate();
    private WebApplication? _server;
    private string _reportedFingerprint = string.Empty;

    public int Port { get; private set; }
    public string Fingerprint => CertificateFingerprint.Compute(_certificate);

    public async Task InitializeAsync()
    {
        _reportedFingerprint = Fingerprint;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(_certificate)));
        _server = builder.Build();
        _server.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers[ApiContract.TokenHeader] != Token)
            {
                ctx.Response.StatusCode = 401;
                return;
            }

            await next(ctx);
        });
        _server.MapGet(ApiContract.Host, () => new HostInfoDto("PC-TLS", 3389, "0.1.0", _reportedFingerprint));
        _server.MapGet(ApiContract.Apps, () => new[] { new AppSummaryDto("notepad", "Bloco de Notas", ApiContract.Icon("notepad")) });
        await _server.StartAsync();
        var address = _server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Port = new Uri(address).Port;
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _certificate.Dispose();
    }

    [Fact]
    public async Task Pairing_returns_host_and_observed_fingerprint()
    {
        var result = await AgentClient.PairAsync("127.0.0.1", Port, Token);

        Assert.Equal("PC-TLS", result.Host.HostName);
        Assert.Equal(Fingerprint, result.ObservedFingerprint);
    }

    [Fact]
    public async Task Pairing_fails_when_reported_fingerprint_differs_from_observed()
    {
        _reportedFingerprint = "DEADBEEF";

        await Assert.ThrowsAsync<PairingMismatchException>(() => AgentClient.PairAsync("127.0.0.1", Port, Token));
    }

    [Fact]
    public async Task Pairing_with_wrong_token_fails()
    {
        await Assert.ThrowsAsync<InvalidTokenException>(() => AgentClient.PairAsync("127.0.0.1", Port, "wrong"));
    }

    [Fact]
    public async Task Pinned_client_works_with_matching_certificate()
    {
        using var client = AgentClient.ForHost(Samples.Host("127.0.0.1") with
        {
            AgentPort = Port, PinnedFingerprint = Fingerprint, ProtectedToken = HostStore.ProtectToken(Token),
        });

        var apps = await client.GetAppsAsync();

        Assert.Equal("notepad", Assert.Single(apps).Alias);
    }

    [Fact]
    public async Task Pinned_client_rejects_a_different_certificate()
    {
        using var client = AgentClient.ForHost(Samples.Host("127.0.0.1") with
        {
            AgentPort = Port, PinnedFingerprint = "00FF", ProtectedToken = HostStore.ProtectToken(Token),
        });

        var ex = await Assert.ThrowsAsync<CertificateChangedException>(() => client.GetAppsAsync());
        Assert.Equal(Fingerprint, ex.ObservedFingerprint);
    }

    [Fact]
    public void Unreadable_protected_token_is_reported_as_invalid_token()
    {
        var host = Samples.Host("127.0.0.1") with { AgentPort = Port, ProtectedToken = Convert.ToBase64String(new byte[64]) };

        var ex = Assert.Throws<InvalidTokenException>(() => AgentClient.ForHost(host));
        Assert.Contains("não pode ser lido", ex.Message);
    }

    [Fact]
    public async Task Unreachable_agent_reports_address_and_port()
    {
        var ex = await Assert.ThrowsAsync<AgentUnreachableException>(() => AgentClient.PairAsync("127.0.0.1", 1, Token));

        Assert.Contains("127.0.0.1:1", ex.Message);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=WRApperture Test Agent", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // SChannel cannot use ephemeral keys; round-trip through PFX.
        return new X509Certificate2(ephemeral.Export(X509ContentType.Pfx));
    }
}

public class AgentClientHttpTests
{
    [Fact]
    public async Task Too_many_attempts_is_mapped()
    {
        using var client = Create(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<TooManyAttemptsException>(() => client.GetAppsAsync());
    }

    [Fact]
    public async Task Other_errors_use_agent_message()
    {
        using var client = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new ApiErrorDto("not_found", "App 'x' não encontrado.")),
        });

        var ex = await Assert.ThrowsAsync<AgentException>(() => client.GetIconAsync("x", 48, null));
        Assert.Equal("App 'x' não encontrado.", ex.Message);
    }

    [Fact]
    public async Task Icon_sends_size_and_etag_and_handles_not_modified()
    {
        HttpRequestMessage? seen = null;
        using var client = Create(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });

        var result = await client.GetIconAsync("notepad", 32, "\"abc\"");

        Assert.True(result.NotModified);
        Assert.Equal("\"abc\"", result.ETag);
        Assert.Equal("/api/apps/notepad/icon?size=32", seen!.RequestUri!.PathAndQuery);
        Assert.Equal("\"abc\"", seen.Headers.IfNoneMatch.Single().ToString());
    }

    [Fact]
    public async Task Icon_returns_bytes_and_new_etag()
    {
        using var client = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"new\"");
            return response;
        });

        var result = await client.GetIconAsync("notepad", 48, null);

        Assert.False(result.NotModified);
        Assert.Equal([1, 2, 3], result.Png);
        Assert.Equal("\"new\"", result.ETag);
    }

    private static AgentClient Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://host:5080") },
            CertificatePinValidator.Pinned("X"), "host", 5080);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
