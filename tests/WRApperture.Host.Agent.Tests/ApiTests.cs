using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WRApperture.Host.Agent.Security;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Tests;

public class ApiTests : IDisposable
{
    private readonly AgentFactory _factory = new();

    public ApiTests()
    {
        _factory.Publisher.Apps.Add(FakePublisher.App("notepad", "Bloco de Notas"));
        _factory.Publisher.Apps.Add(FakePublisher.App("calc", "Calculadora"));
        _factory.Publisher.Apps.Add(FakePublisher.App("hidden", "Oculto", visible: false));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Health_is_open_and_reveals_nothing()
    {
        var response = await _factory.CreateClient().GetAsync(ApiContract.Health);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(ApiContract.Apps)]
    [InlineData(ApiContract.Host)]
    [InlineData("/api/apps/notepad/icon")]
    public async Task Data_routes_require_token(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
        Assert.Equal(ApiContract.Errors.Unauthorized, error!.Error);
    }

    [Fact]
    public async Task Wrong_token_is_rejected()
    {
        var response = await _factory.CreateAuthorizedClient("wrong").GetAsync(ApiContract.Apps);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unpaired_agent_rejects_everything()
    {
        _factory.Settings.CurrentToken = null;

        var response = await _factory.CreateAuthorizedClient().GetAsync(ApiContract.Apps);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Apps_lists_visible_apps_sorted_without_exposing_paths()
    {
        var response = await _factory.CreateAuthorizedClient().GetAsync(ApiContract.Apps);
        var body = await response.Content.ReadAsStringAsync();
        var apps = await response.Content.ReadFromJsonAsync<AppSummaryDto[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Bloco de Notas", "Calculadora"], apps!.Select(a => a.DisplayName));
        Assert.Equal("/api/apps/notepad/icon", apps![0].IconUrl);
        Assert.DoesNotContain("Secret", body);
        Assert.Contains("\"displayName\"", body); // camelCase contract
    }

    [Fact]
    public async Task Apps_reflects_changes_without_restart()
    {
        var client = _factory.CreateAuthorizedClient();
        _factory.Publisher.Apps.RemoveAll(a => a.Alias == "calc");

        var apps = await client.GetFromJsonAsync<AppSummaryDto[]>(ApiContract.Apps);

        Assert.Equal(["notepad"], apps!.Select(a => a.Alias));
    }

    [Fact]
    public async Task Host_returns_connection_info()
    {
        var info = await _factory.CreateAuthorizedClient().GetFromJsonAsync<HostInfoDto>(ApiContract.Host);

        Assert.Equal("PC-TESTE", info!.HostName);
        Assert.Equal(3390, info.RdpPort);
        Assert.Equal(AgentFactory.Fingerprint, info.CertificateFingerprint);
        Assert.False(string.IsNullOrWhiteSpace(info.AgentVersion));
        Assert.Contains(AgentFeatures.DesktopSession, info.Features!);
    }

    [Fact]
    public async Task Regenerated_token_invalidates_old_one_immediately()
    {
        var oldClient = _factory.CreateAuthorizedClient();
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync(ApiContract.Apps)).StatusCode);

        var newToken = _factory.Settings.RegenerateToken();

        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync(ApiContract.Apps)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateAuthorizedClient(newToken).GetAsync(ApiContract.Apps)).StatusCode);
    }

    [Fact]
    public async Task Too_many_failures_block_the_source_temporarily()
    {
        var bad = _factory.CreateAuthorizedClient("wrong");
        for (var i = 0; i < FailedAuthLimiter.MaxFailures; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await bad.GetAsync(ApiContract.Apps)).StatusCode);
        }

        // Even the right token is refused while blocked.
        var blocked = await _factory.CreateAuthorizedClient().GetAsync(ApiContract.Apps);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(ApiContract.Errors.TooManyAttempts, (await blocked.Content.ReadFromJsonAsync<ApiErrorDto>())!.Error);

        _factory.Time.Now += FailedAuthLimiter.BlockDuration + TimeSpan.FromSeconds(1);
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateAuthorizedClient().GetAsync(ApiContract.Apps)).StatusCode);
    }

    [Fact]
    public async Task Failures_outside_the_window_do_not_accumulate()
    {
        var bad = _factory.CreateAuthorizedClient("wrong");
        for (var i = 0; i < FailedAuthLimiter.MaxFailures - 1; i++)
        {
            await bad.GetAsync(ApiContract.Apps);
        }

        _factory.Time.Now += FailedAuthLimiter.Window + TimeSpan.FromSeconds(1);
        await bad.GetAsync(ApiContract.Apps);

        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateAuthorizedClient().GetAsync(ApiContract.Apps)).StatusCode);
    }

    [Fact]
    public async Task Icon_returns_png_with_etag_and_cache_headers()
    {
        var response = await _factory.CreateAuthorizedClient().GetAsync("/api/apps/notepad/icon?size=32");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(32, (await response.Content.ReadAsByteArrayAsync())[^1]);
        Assert.NotNull(response.Headers.ETag);
        Assert.True(response.Headers.CacheControl!.Private);
    }

    [Fact]
    public async Task Icon_defaults_to_48px()
    {
        var bytes = await _factory.CreateAuthorizedClient().GetByteArrayAsync("/api/apps/notepad/icon");

        Assert.Equal(48, bytes[^1]);
    }

    [Fact]
    public async Task Icon_honors_if_none_match_and_uses_cache()
    {
        var client = _factory.CreateAuthorizedClient();
        var first = await client.GetAsync("/api/apps/notepad/icon");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/apps/notepad/icon");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(1, _factory.Icons.Calls);
    }

    [Theory]
    [InlineData("/api/apps/nope/icon", HttpStatusCode.NotFound)]
    [InlineData("/api/apps/hidden/icon", HttpStatusCode.NotFound)]
    [InlineData("/api/apps/notepad/icon?size=8", HttpStatusCode.BadRequest)]
    [InlineData("/api/apps/notepad/icon?size=512", HttpStatusCode.BadRequest)]
    public async Task Icon_errors(string route, HttpStatusCode expected)
    {
        var response = await _factory.CreateAuthorizedClient().GetAsync(route);

        Assert.Equal(expected, response.StatusCode);
    }
}
