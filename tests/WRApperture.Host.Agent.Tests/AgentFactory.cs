using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Agent.Certificates;
using WRApperture.Host.Agent.Updates;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Abstractions;
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
    internal FakePreparation Preparation { get; } = new();
    internal FakeAccounts Accounts { get; } = new();
    internal FakeProcesses Processes { get; } = new();
    internal FakeStartupPrograms Startup { get; } = new();

    /// <summary>The host app the agent opens to approve pairing requests (never started: see <see cref="FakeSessions.Started"/>).</summary>
    internal const string ApproverPath = @"C:\Fake\WRApperture.Host.exe";

    /// <summary>Requests come from this address unless they carry <see cref="RemoteAddressHeader"/>.</summary>
    internal const string RemoteAddressHeader = "X-Test-Remote-Address";

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
            services.AddSingleton<IHostPreparation>(Preparation);
            services.AddSingleton<ILocalAccounts>(Accounts);
            services.AddSingleton<IProcessList>(Processes);
            services.AddSingleton<WRApperture.Host.Core.StartWith.IStartupPrograms>(Startup);
            services.AddSingleton(new Pairing.PairingOptions { ApproverPath = ApproverPath });
            services.AddSingleton<IStartupFilter, RemoteAddressFilter>();
            services.AddSingleton(new HostUpdaterOptions
            {
                CurrentVersion = CurrentVersion,
                FirstCheckDelay = Timeout.InfiniteTimeSpan,
                CanInstallUnattended = true,
                PrepareDownloadFolder = () => @"C:\Fake\updates\agent",
            });
        });
    }

    /// <summary>A client whose requests come from <paramref name="address"/> (TestServer has no real remote address).</summary>
    public HttpClient CreateClientFrom(string address)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(RemoteAddressHeader, address);
        return client;
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

/// <summary>Sets the connection's remote address: loopback by default, or the test header's value.</summary>
internal sealed class RemoteAddressFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = context.Request.Headers.TryGetValue(AgentFactory.RemoteAddressHeader, out var value)
                ? System.Net.IPAddress.Parse(value.ToString())
                : System.Net.IPAddress.Loopback;
            return nextMiddleware(context);
        });
        next(app);
    };
}

internal sealed class FakeSettings : IAgentSettingsStore
{
    public string? CurrentToken { get; set; } = AgentFactory.Token;
    public bool AutoUpdate { get; set; } = true;
    public List<(PairedClient Client, string Token)> Clients { get; } = [];

    public AgentSettings Load() => new(ApiContract.DefaultPort, CurrentToken is not null || Clients.Count > 0, "THUMB", AutoUpdate);

    public string RegenerateToken() => CurrentToken = Guid.NewGuid().ToString("N");

    public bool VerifyToken(string? token) =>
        token is not null && (token == CurrentToken || Clients.Any(c => c.Token == token));

    public (PairedClient Client, string Token) AddClient(string name, string? user)
    {
        var entry = (new PairedClient(Guid.NewGuid(), name, user, DateTimeOffset.UtcNow), Guid.NewGuid().ToString("N"));
        Clients.Add(entry);
        return entry;
    }

    public IReadOnlyList<PairedClient> ListClients() => Clients.Select(c => c.Client).ToList();

    public bool RemoveClient(Guid id) => Clients.RemoveAll(c => c.Client.Id == id) > 0;

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

    public List<(int Session, string Program, string? Arguments)> Started { get; } = [];

    public int StartProcess(int sessionId, string executable, string? arguments)
    {
        Started.Add((sessionId, executable, arguments));
        return 4242;
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

internal sealed class FakePreparation : IHostPreparation
{
    public List<ReadinessCheck> Checks { get; } = [];
    public int Calls { get; private set; }

    public static ReadinessCheck Pending(ReadinessCheckId id) => new(id, id.ToString(), ReadinessState.Pending, "x", "y");

    public IReadOnlyList<ReadinessCheck> Check()
    {
        Calls++;
        return Checks.ToList();
    }

    public IReadOnlyList<ReadinessCheck> Prepare() => throw new NotSupportedException();

    public IReadOnlyList<ReadinessCheck> Fix(ReadinessCheckId id) => throw new NotSupportedException();

    public IReadOnlyList<ReadinessCheck> SetAccountPassword(string password) => throw new NotSupportedException();

    public IReadOnlyList<ReadinessCheck> Revert() => throw new NotSupportedException();
}

internal sealed class FakeAccounts : ILocalAccounts
{
    public LocalAccount? Console { get; set; } = new("joao", "PC-TESTE", "S-1-5-21-1-2-3-1001", true, null, true, false);

    public LocalAccount? ConsoleUser() => Console;

    public PasswordStatus GetPasswordStatus(LocalAccount account) => PasswordStatus.Set;

    public void SetPassword(LocalAccount account, string password) => throw new NotSupportedException();

    public void AddToRemoteDesktopUsers(string sid) => throw new NotSupportedException();

    public void RemoveFromRemoteDesktopUsers(string sid) => throw new NotSupportedException();
}

internal sealed class FakeProcesses : IProcessList
{
    public List<RunningProcess> All { get; } = [];

    public IReadOnlyList<RunningProcess> InSessions(IReadOnlySet<int> sessionIds) => All.Where(p => sessionIds.Contains(p.SessionId)).ToList();

    public List<int> Stopped { get; } = [];

    public bool Stop(int processId, string processName)
    {
        Stopped.Add(processId);
        return true;
    }
}

internal sealed class FakeStartupPrograms : WRApperture.Host.Core.StartWith.IStartupPrograms
{
    public List<WRApperture.Host.Core.StartWith.StartupProgram> Programs { get; } = [];

    public IReadOnlyList<WRApperture.Host.Core.StartWith.StartupProgram> List(string? userSid) => Programs;

    public IReadOnlyList<WRApperture.Host.Core.StartWith.StartupProgram> TrayProgramsIn(string folder) => [];
}
