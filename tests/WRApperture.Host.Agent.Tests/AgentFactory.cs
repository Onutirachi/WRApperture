using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Agent.Certificates;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Agent;
using WRApperture.Host.Core.HostInfo;
using WRApperture.Host.Core.Models;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Tests;

/// <summary>Agent under TestServer with in-memory fakes (no registry, no cert store).</summary>
public sealed class AgentFactory : WebApplicationFactory<Program>
{
    public const string Token = "test-token-123";
    public const string Fingerprint = "ABCDEF0123";

    internal FakePublisher Publisher { get; } = new();
    internal FakeSettings Settings { get; } = new();
    internal FakeIcons Icons { get; } = new();
    internal ManualTimeProvider Time { get; } = new();
    internal FakeSessions Sessions { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAppPublisher>(Publisher);
            services.AddSingleton<IAgentSettingsStore>(Settings);
            services.AddSingleton<IIconProvider>(Icons);
            services.AddSingleton<IHostInfoProvider>(new FakeHostInfo());
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<WRApperture.Host.Core.Sessions.ISessionProvider>(Sessions);
            services.AddSingleton(new AgentIdentity { Fingerprint = Fingerprint });
        });
    }

    public HttpClient CreateAuthorizedClient(string token = Token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiContract.TokenHeader, token);
        return client;
    }
}

internal sealed class FakePublisher : IAppPublisher
{
    public List<PublishedApp> Apps { get; } = [];

    public static PublishedApp App(string alias, string name, bool visible = true) =>
        new(alias, name, $@"C:\Secret\{alias}.exe", null, $@"C:\Secret\{alias}.exe", 0, CommandLinePolicy.None, null, visible, true);

    public IReadOnlyList<PublishedApp> List() => Apps.OrderBy(a => a.DisplayName).ToList();

    public PublishedApp? Get(string alias) =>
        Apps.FirstOrDefault(a => string.Equals(a.Alias, alias, StringComparison.OrdinalIgnoreCase));

    public PublishResult Publish(PublishRequest request) => throw new NotSupportedException();

    public bool Unpublish(string alias) => throw new NotSupportedException();
}

internal sealed class FakeSettings : IAgentSettingsStore
{
    public string? CurrentToken { get; set; } = AgentFactory.Token;

    public AgentSettings Load() => new(ApiContract.DefaultPort, CurrentToken is not null, "THUMB");

    public string RegenerateToken() => CurrentToken = Guid.NewGuid().ToString("N");

    public bool VerifyToken(string? token) => CurrentToken is not null && token == CurrentToken;

    public void SetPort(int port) { }

    public void SetCertificateThumbprint(string thumbprint) { }

    public void Reset() => CurrentToken = null;
}

internal sealed class FakeIcons : IIconProvider
{
    public int Calls { get; private set; }

    public byte[] GetPng(string iconPath, int iconIndex, int size = 48)
    {
        Calls++;
        return [0x89, 0x50, 0x4E, 0x47, (byte)size];
    }
}

internal sealed class FakeHostInfo : IHostInfoProvider
{
    public HostInfo Get() => new("PC-TESTE", 3390);
}

internal sealed class FakeSessions : WRApperture.Host.Core.Sessions.ISessionProvider
{
    public List<WRApperture.Host.Core.Sessions.HostSession> All { get; } = [];
    public List<int> LoggedOff { get; } = [];
    public List<int> ShellStarted { get; } = [];

    public IReadOnlyList<WRApperture.Host.Core.Sessions.HostSession> List() => All.ToList();

    public void Logoff(int sessionId)
    {
        LoggedOff.Add(sessionId);
        All.RemoveAll(s => s.Id == sessionId);
    }

    public void StartDesktopShell(int sessionId)
    {
        ShellStarted.Add(sessionId);
        var i = All.FindIndex(s => s.Id == sessionId);
        All[i] = All[i] with { Kind = WRApperture.Host.Core.Sessions.SessionKind.Desktop };
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
