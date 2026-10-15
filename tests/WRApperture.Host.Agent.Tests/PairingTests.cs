using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WRApperture.Host.Agent.Discovery;
using WRApperture.Host.Agent.Pairing;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Sessions;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Tests;

public class PairingTests : IDisposable
{
    private readonly AgentFactory _factory = new();
    private readonly string _nonce = PairingCode.NewNonce();

    public PairingTests() =>
        _factory.Sessions.All.Add(new HostSession(1, "maria", "PC-TESTE", SessionConnectState.Active, true, SessionKind.Desktop));

    public void Dispose() => _factory.Dispose();

    private async Task<(HttpResponseMessage Response, PairingTicketDto? Ticket)> RequestAsync(string from = "192.168.0.23", string name = "NOTEBOOK-JOAO")
    {
        var response = await _factory.CreateClientFrom(from).PostAsJsonAsync(ApiContract.PairingRequests, new PairingRequestDto(name, "joao", _nonce));
        return (response, response.StatusCode == HttpStatusCode.Accepted ? await response.Content.ReadFromJsonAsync<PairingTicketDto>() : null);
    }

    /// <summary>The secret the agent gave the approval window it opened.</summary>
    private string SecretOf(string id) =>
        _factory.Sessions.Started.Single(s => s.Arguments!.Contains(id)).Arguments!.Split(' ')[2];

    private HttpClient Approver(string id, string? secret = null)
    {
        var client = _factory.CreateClient(); // loopback
        client.DefaultRequestHeaders.Add(ApiContract.ApprovalHeader, secret ?? SecretOf(id));
        return client;
    }

    private Task<PairingStatusDto?> StatusAsync(string id) =>
        _factory.CreateClientFrom("192.168.0.23").GetFromJsonAsync<PairingStatusDto>(ApiContract.PairingRequest(id));

    [Fact]
    public async Task Request_opens_the_approval_window_on_the_console_with_the_same_code_as_the_client()
    {
        var (response, ticket) = await RequestAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var started = Assert.Single(_factory.Sessions.Started);
        Assert.Equal(1, started.Session);
        Assert.Equal(AgentFactory.ApproverPath, started.Program);
        Assert.StartsWith($"--pairing {ticket!.Id} ", started.Arguments);

        var details = await Approver(ticket.Id).GetFromJsonAsync<PairingDetailsDto>(ApiContract.PairingApproval(ticket.Id));
        Assert.Equal("NOTEBOOK-JOAO", details!.ClientName);
        Assert.Equal("joao", details.ClientUser);
        Assert.Equal("192.168.0.23", details.Address);
        Assert.Equal(PairingCode.Compute(AgentFactory.Fingerprint, _nonce), details.Code);
        Assert.Equal(PairingStates.Pending, (await StatusAsync(ticket.Id))!.State);
    }

    [Fact]
    public async Task Approved_client_gets_its_own_token_exactly_once_and_it_opens_the_api()
    {
        var (_, ticket) = await RequestAsync();

        var decision = await Approver(ticket!.Id).PostAsJsonAsync(ApiContract.PairingApproval(ticket.Id), new PairingDecisionDto(true));
        Assert.Equal(HttpStatusCode.NoContent, decision.StatusCode);

        var first = await StatusAsync(ticket.Id);
        Assert.Equal(PairingStates.Approved, first!.State);
        Assert.NotNull(first.Token);
        Assert.Null((await StatusAsync(ticket.Id))!.Token);

        var client = Assert.Single(_factory.Settings.Clients).Client;
        Assert.Equal("NOTEBOOK-JOAO", client.Name);
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateAuthorizedClient(first.Token!).GetAsync(ApiContract.Apps)).StatusCode);
    }

    [Fact]
    public async Task Pairing_again_from_the_same_computer_replaces_its_device_and_old_token()
    {
        async Task<string> PairAsync(string user)
        {
            var response = await _factory.CreateClientFrom("192.168.0.23")
                .PostAsJsonAsync(ApiContract.PairingRequests, new PairingRequestDto("NOTEBOOK-JOAO", user, PairingCode.NewNonce()));
            var ticket = (await response.Content.ReadFromJsonAsync<PairingTicketDto>())!;
            await Approver(ticket.Id).PostAsJsonAsync(ApiContract.PairingApproval(ticket.Id), new PairingDecisionDto(true));
            return (await StatusAsync(ticket.Id))!.Token!;
        }

        var first = await PairAsync("joao");
        var second = await PairAsync("JOAO");
        await PairAsync("maria"); // another user of that computer is another device

        Assert.Equal(["joao", "maria"], _factory.Settings.Clients.Select(c => c.Client.User!.ToLowerInvariant()).Order());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateAuthorizedClient(first).GetAsync(ApiContract.Apps)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateAuthorizedClient(second).GetAsync(ApiContract.Apps)).StatusCode);
    }

    [Fact]
    public async Task Denied_request_creates_no_client()
    {
        var (_, ticket) = await RequestAsync();

        await Approver(ticket!.Id).PostAsJsonAsync(ApiContract.PairingApproval(ticket.Id), new PairingDecisionDto(false));

        Assert.Equal(PairingStates.Denied, (await StatusAsync(ticket.Id))!.State);
        Assert.Empty(_factory.Settings.Clients);
    }

    [Fact]
    public async Task Approval_only_from_this_pc_with_the_secret()
    {
        var (_, ticket) = await RequestAsync();
        var url = ApiContract.PairingApproval(ticket!.Id);

        var remote = _factory.CreateClientFrom("192.168.0.23");
        remote.DefaultRequestHeaders.Add(ApiContract.ApprovalHeader, SecretOf(ticket.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await remote.PostAsJsonAsync(url, new PairingDecisionDto(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Approver(ticket.Id, "wrong").PostAsJsonAsync(url, new PairingDecisionDto(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Approver(ticket.Id, "wrong").GetAsync(url)).StatusCode);

        Assert.Empty(_factory.Settings.Clients);
        Assert.Equal(PairingStates.Pending, (await StatusAsync(ticket.Id))!.State);
    }

    [Fact]
    public async Task Request_expires_and_can_no_longer_be_approved()
    {
        var (_, ticket) = await RequestAsync();
        var approver = Approver(ticket!.Id);

        _factory.Time.Now += TimeSpan.FromSeconds(ticket.ExpiresInSeconds);

        Assert.Equal(PairingStates.Expired, (await StatusAsync(ticket.Id))!.State);
        Assert.Equal(HttpStatusCode.NotFound,
            (await approver.PostAsJsonAsync(ApiContract.PairingApproval(ticket.Id), new PairingDecisionDto(true))).StatusCode);
        Assert.Empty(_factory.Settings.Clients);
    }

    [Fact]
    public async Task Nobody_at_the_pc_means_no_approver()
    {
        _factory.Sessions.All.Clear();

        var (response, _) = await RequestAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ApiContract.Errors.NoApprover, (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Error);
        Assert.Empty(_factory.Sessions.Started);
    }

    [Fact]
    public async Task One_pending_request_per_address_and_a_few_in_total()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync("192.168.0.10")).Response.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestAsync("192.168.0.10")).Response.StatusCode);

        for (var i = 1; i < PairingService.MaxPending; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync($"192.168.0.{20 + i}")).Response.StatusCode);
        }

        var (response, _) = await RequestAsync("192.168.0.99");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(ApiContract.Errors.PairingLimited, (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Error);
    }

    [Fact]
    public async Task An_address_cannot_keep_asking()
    {
        for (var i = 0; i < PairingService.MaxPerAddress; i++)
        {
            var (_, ticket) = await RequestAsync();
            await Approver(ticket!.Id).PostAsJsonAsync(ApiContract.PairingApproval(ticket.Id), new PairingDecisionDto(false));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestAsync()).Response.StatusCode);

        _factory.Time.Now += PairingService.AddressWindow + TimeSpan.FromSeconds(1);
        Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync()).Response.StatusCode);
    }

    [Fact]
    public async Task Names_are_cleaned_and_bounded()
    {
        var (_, ticket) = await RequestAsync(name: "EVIL\u0007‮" + new string('X', 100));

        var details = await Approver(ticket!.Id).GetFromJsonAsync<PairingDetailsDto>(ApiContract.PairingApproval(ticket.Id));

        Assert.StartsWith("EVILXXX", details!.ClientName);
        Assert.Equal(PairingService.MaxNameLength, details.ClientName.Length);
    }

    [Theory]
    [InlineData("", "AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("PC", "not-base64")]
    [InlineData("PC", "AAAA")]
    public async Task Invalid_requests_are_rejected(string name, string nonce)
    {
        var response = await _factory.CreateClientFrom("192.168.0.23")
            .PostAsJsonAsync(ApiContract.PairingRequests, new PairingRequestDto(name, null, nonce));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Sessions.Started);
    }

    [Fact]
    public async Task Host_info_tells_what_keeps_remote_access_from_working_and_who_to_sign_in_as()
    {
        _factory.Preparation.Checks.AddRange(
        [
            FakePreparation.Pending(ReadinessCheckId.NetworkProfile),
            FakePreparation.Pending(ReadinessCheckId.RdpEnabled),
            FakePreparation.Pending(ReadinessCheckId.RdpService),
            FakePreparation.Pending(ReadinessCheckId.LanguageBarHidden),
            new(ReadinessCheckId.Sleep, "sleep", ReadinessState.Advice, "x", "y"),
        ]);

        var client = _factory.CreateAuthorizedClient();
        var info = await client.GetFromJsonAsync<HostInfoDto>(ApiContract.Host);
        await client.GetFromJsonAsync<HostInfoDto>(ApiContract.Host);

        Assert.Equal([HostIssues.PublicNetwork, HostIssues.RdpDisabled], info!.Issues!.Order());
        Assert.Equal(@"PC-TESTE\joao", info.SuggestedUser);
        Assert.False(info.MicrosoftAccount);
        Assert.Contains(AgentFeatures.Pairing, info.Features!);
        Assert.Contains(AgentFeatures.Readiness, info.Features!);
        Assert.Equal(1, _factory.Preparation.Calls); // cached
    }

    [Fact]
    public async Task Microsoft_account_is_suggested_by_its_email()
    {
        _factory.Accounts.Console = _factory.Accounts.Console! with { MicrosoftAccount = "joao@outlook.com" };

        var info = await _factory.CreateAuthorizedClient().GetFromJsonAsync<HostInfoDto>(ApiContract.Host);

        Assert.Equal("joao@outlook.com", info!.SuggestedUser);
        Assert.True(info.MicrosoftAccount);
    }

    [Fact]
    public void Discovery_answers_only_requests_and_never_with_more_bytes()
    {
        var responder = _factory.Services.GetServices<IHostedService>().OfType<DiscoveryResponder>().Single();

        Assert.Null(responder.BuildReply("hello"u8.ToArray()));

        var request = WRApperture.Shared.Discovery.BuildRequest();
        var reply = responder.BuildReply(request);
        Assert.NotNull(reply);
        Assert.True(reply.Length <= request.Length);

        var decoded = WRApperture.Shared.Discovery.DecodeReply(reply);
        Assert.Equal(Environment.MachineName, decoded!.HostName);
        Assert.Equal(AgentFactory.Fingerprint, decoded.Fingerprint);
        Assert.Equal(ApiContract.DefaultPort, decoded.AgentPort);
        Assert.Contains(AgentFeatures.Pairing, decoded.Features);
    }
}
