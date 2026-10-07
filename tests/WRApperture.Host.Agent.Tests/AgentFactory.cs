using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Agent.Certificates;
using WRApperture.Host.Agent.Updates;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Agent;
using WRApperture.Host.Core.HostInfo;
using WRApperture.Host.Core.Models;
using WRApperture.Shared;
using WRApperture.Shared.Updates;

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
    internal FakeUpdateChecker Updates { get; } = new();
    internal FakeSetupRunner Setups { get; } = new();

    /// <summary>The agent's own version in these tests; the scheduled loop never fires on its own.</summary>
    internal static readonly Version CurrentVersion = new(1, 0, 0);

    internal HostUpdater Updater => Services.GetRequiredService<HostUpdater>();

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
            services.AddSingleton<IUpdateChecker>(Updates);
            services.AddSingleton<IHostSetupRunner>(Setups);
            services.AddSingleton(new HostUpdaterOptions
            {
                CurrentVersion = CurrentVersion,
                FirstCheckDelay = Timeout.InfiniteTimeSpan,
                CanInstallUnattended = true,
                PrepareDownloadFolder = () => @"C:\Fake\updates\agent",
            });
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
    public bool AutoUpdate { get; set; } = true;

    public AgentSettings Load() => new(ApiContract.DefaultPort, CurrentToken is not null, "THUMB", AutoUpdate);

    public string RegenerateToken() => CurrentToken = Guid.NewGuid().ToString("N");

    public bool VerifyToken(string? token) => CurrentToken is not null && token == CurrentToken;

    public void SetPort(int port) { }

    public void SetCertificateThumbprint(string thumbprint) { }

    public void SetAutoUpdate(bool enabled) => AutoUpdate = enabled;

    public void Reset() => CurrentToken = null;
}

/// <summary>Stands in for GitHub: never touches the network.</summary>
internal sealed class FakeUpdateChecker : IUpdateChecker
{
    public ReleaseInfo? Latest { get; set; }
    public Exception? CheckError { get; set; }
    public Exception? DownloadError { get; set; }
    public int Checks { get; private set; }
    public List<(UpdateProduct Product, string Folder)> Downloads { get; } = [];

    public static ReleaseInfo Release(string version) => new(Version.Parse(version), "v" + version, "notes", null, []);

    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        Checks++;
        return CheckError is null ? Task.FromResult(Latest) : Task.FromException<ReleaseInfo?>(CheckError);
    }

    public Task<VerifiedSetup> DownloadVerifiedAsync(ReleaseInfo release, UpdateProduct product, string targetDirectory,
        CancellationToken ct = default)
    {
        Downloads.Add((product, targetDirectory));
        return DownloadError is null
            ? Task.FromResult(new VerifiedSetup(Path.Combine(targetDirectory, UpdateSource.SetupAssetName(product, release.Version)), "abc", release.Version))
            : Task.FromException<VerifiedSetup>(DownloadError);
    }
}

/// <summary>Records setups instead of starting them; <see cref="Gate"/> keeps one "running".</summary>
internal sealed class FakeSetupRunner : IHostSetupRunner
{
    public List<VerifiedSetup> Runs { get; } = [];
    public TaskCompletionSource<int>? Gate { get; set; }
    public int ExitCode { get; set; }

    public Task<int> RunAsync(VerifiedSetup setup, CancellationToken ct)
    {
        Runs.Add(setup);
        return Gate?.Task ?? Task.FromResult(ExitCode);
    }
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
    public List<int> Disconnected { get; } = [];

    public void Disconnect(int sessionId)
    {
        Disconnected.Add(sessionId);
        var i = All.FindIndex(s => s.Id == sessionId);
        All[i] = All[i] with { State = WRApperture.Host.Core.Sessions.SessionConnectState.Disconnected };
    }

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
