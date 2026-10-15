using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
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
using WRApperture.Client.Core.Discovery;
using WRApperture.Shared;

namespace WRApperture.Client.Tests;

/// <summary>Pairing by approval over real TLS against a minimal loopback agent.</summary>
public sealed class PairingClientTests : IAsyncLifetime
{
    private const string IssuedToken = "client-token-1";
    private readonly X509Certificate2 _certificate = CreateCertificate();
    private readonly ConcurrentQueue<PairingStatusDto> _answers = new();
    private WebApplication? _server;
    private string? _nonce;
    private (int Status, string Error, string Message)? _refusal;

    private int Port { get; set; }
    private string Fingerprint => CertificateFingerprint.Compute(_certificate);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.UseHttps(_certificate)));
        _server = builder.Build();
        _server.MapPost(ApiContract.PairingRequests, (PairingRequestDto request) =>
        {
            if (_refusal is { } refusal)
            {
                return Results.Json(new ApiErrorDto(refusal.Error, refusal.Message), statusCode: refusal.Status);
            }

            _nonce = request.Nonce;
            return Results.Accepted(ApiContract.PairingRequest("abc"), new PairingTicketDto("abc", 120));
        });
        _server.MapGet(ApiContract.PairingRequests + "/{id}", (string id) =>
            id == "abc" && _answers.TryDequeue(out var answer) ? Results.Ok(answer) : Results.NotFound());
        _server.MapGet(ApiContract.Host, (HttpContext context) =>
            context.Request.Headers[ApiContract.TokenHeader] == IssuedToken
                ? Results.Ok(new HostInfoDto("PC-SALA", 3389, "1.0.0", Fingerprint, [AgentFeatures.Pairing], [], @"PC-SALA\joao"))
                : Results.Unauthorized());
        await _server.StartAsync();
        Port = new Uri(_server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
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
    public async Task Approved_pairing_shows_the_agents_code_and_ends_with_the_new_token()
    {
        _answers.Enqueue(new PairingStatusDto(PairingStates.Pending));
        _answers.Enqueue(new PairingStatusDto(PairingStates.Approved, IssuedToken));

        var pending = await AgentClient.RequestPairingAsync("127.0.0.1", Port, "NOTEBOOK-JOAO", "joao");
        Assert.Equal(PairingCode.Compute(Fingerprint, _nonce!), pending.Code);
        Assert.Equal(Fingerprint, pending.ObservedFingerprint);

        var token = await AgentClient.WaitForApprovalAsync(pending);
        Assert.Equal(IssuedToken, token);

        var result = await AgentClient.CompleteApprovedPairingAsync(pending, token);
        Assert.Equal("PC-SALA", result.Host.HostName);
        Assert.Equal(@"PC-SALA\joao", result.Host.SuggestedUser);
        Assert.Equal(Fingerprint, result.ObservedFingerprint);
    }

    [Theory]
    [InlineData(PairingStates.Denied)]
    [InlineData(PairingStates.Expired)]
    public async Task Denied_or_expired_requests_refuse(string state)
    {
        _answers.Enqueue(new PairingStatusDto(state));
        var pending = await AgentClient.RequestPairingAsync("127.0.0.1", Port, "NOTEBOOK-JOAO", null);

        await Assert.ThrowsAsync<PairingRefusedException>(() => AgentClient.WaitForApprovalAsync(pending));
    }

    [Fact]
    public async Task Forgotten_request_counts_as_expired()
    {
        var pending = await AgentClient.RequestPairingAsync("127.0.0.1", Port, "NOTEBOOK-JOAO", null);

        await Assert.ThrowsAsync<PairingRefusedException>(() => AgentClient.WaitForApprovalAsync(pending)); // 404
    }

    [Fact]
    public async Task Refused_request_carries_the_agents_message()
    {
        _refusal = (StatusCodes.Status409Conflict, ApiContract.Errors.NoApprover, "Ninguém está usando o PC remoto.");

        var ex = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            AgentClient.RequestPairingAsync("127.0.0.1", Port, "NOTEBOOK-JOAO", null));

        Assert.Equal("Ninguém está usando o PC remoto.", ex.Message);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // SChannel cannot use ephemeral keys; round-trip through PFX.
        return new X509Certificate2(ephemeral.Export(X509ContentType.Pfx));
    }
}

public sealed class HostDiscoveryTests : IDisposable
{
    private readonly UdpClient _responder = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource _stop = new();

    private int Port => ((IPEndPoint)_responder.Client.LocalEndPoint!).Port;

    public void Dispose()
    {
        _stop.Cancel();
        _responder.Dispose();
        _stop.Dispose();
    }

    /// <summary>Answers each request with garbage, then the same host twice (duplicates must collapse).</summary>
    private async Task RespondAsync()
    {
        var reply = Discovery.EncodeReply(new DiscoveryReplyDto("PC-SALA", 5080, "ABCD", "1.0.0", [AgentFeatures.Pairing]))!;
        try
        {
            while (true)
            {
                var request = await _responder.ReceiveAsync(_stop.Token);
                if (!Discovery.IsRequest(request.Buffer))
                {
                    continue;
                }

                await _responder.SendAsync("not json"u8.ToArray(), request.RemoteEndPoint, _stop.Token);
                await _responder.SendAsync(reply, request.RemoteEndPoint, _stop.Token);
                await _responder.SendAsync(reply, request.RemoteEndPoint, _stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    [Fact]
    public async Task Scan_lists_each_answering_host_once()
    {
        _ = RespondAsync();
        var discovery = new HostDiscovery(Port, () => [IPAddress.Loopback]);

        var hosts = await discovery.ScanAsync(TimeSpan.FromMilliseconds(800));

        var host = Assert.Single(hosts);
        Assert.Equal("PC-SALA", host.HostName);
        Assert.Equal("127.0.0.1", host.Address);
        Assert.Equal(5080, host.AgentPort);
        Assert.True(host.SupportsPairing);
    }

    [Fact]
    public async Task Scan_without_answers_is_empty()
    {
        var discovery = new HostDiscovery(Port, () => [IPAddress.Loopback]);

        Assert.Empty(await discovery.ScanAsync(TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task Address_is_the_ip_when_the_name_does_not_resolve_to_it()
    {
        var discovery = new HostDiscovery(Port, () => [IPAddress.Loopback]);
        var host = new DiscoveredHost("nao-existe-wrapperture-teste", "192.0.2.10", 5080, "ABCD", "1.0.0", []);

        Assert.Equal("192.0.2.10", await discovery.ChooseAddressAsync(host));
        Assert.False(host.SupportsPairing);
    }

    [Fact]
    public void Requests_are_padded_and_replies_never_larger()
    {
        var request = Discovery.BuildRequest();
        var reply = Discovery.EncodeReply(new DiscoveryReplyDto(new string('X', 63), 5080, new string('A', 64), "10.20.30",
            [AgentFeatures.DesktopSession, AgentFeatures.SelfUpdate, AgentFeatures.Readiness, AgentFeatures.Pairing]));

        Assert.True(Discovery.IsRequest(request));
        Assert.False(Discovery.IsRequest(request.AsSpan(0, 100)));
        Assert.NotNull(reply);
        Assert.True(reply.Length <= request.Length);
        Assert.Null(Discovery.DecodeReply("{}"u8));
    }

    [Fact]
    public void Pairing_code_depends_on_fingerprint_and_nonce()
    {
        var nonce = PairingCode.NewNonce();
        var code = PairingCode.Compute("ABCD", nonce);

        Assert.Matches(@"^\d{3} \d{3}$", code);
        Assert.Equal(code, PairingCode.Compute("abcd", nonce));
        Assert.NotEqual(code, PairingCode.Compute("ABCE", nonce));
        Assert.NotEqual(code, PairingCode.Compute("ABCD", PairingCode.NewNonce()));
    }
}
