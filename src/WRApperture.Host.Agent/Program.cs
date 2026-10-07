using Microsoft.Extensions.Hosting.WindowsServices;
using WRApperture.Host.Agent.Certificates;
using WRApperture.Host.Agent.Endpoints;
using WRApperture.Host.Agent.Icons;
using WRApperture.Host.Agent.Security;
using WRApperture.Host.Agent.Sessions;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Agent;
using WRApperture.Shared;
using Serilog;
using Serilog.Events;

const string ServiceName = "WRAppertureAgent";
const string TestingEnvironment = "Testing";

// Captures failures before the host's own logger exists (e.g. certificate errors during Build).
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WRApperture", "logs", "agent-startup-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Host.UseWindowsService(o => o.ServiceName = ServiceName);
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.WithProperty("App", "agent")
        .WriteTo.Console();

    if (!context.HostingEnvironment.IsEnvironment(TestingEnvironment))
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WRApperture", "logs");
        configuration.WriteTo.File(Path.Combine(logDir, "agent-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
    }

    if (WindowsServiceHelpers.IsWindowsService())
    {
        configuration.WriteTo.EventLog(ServiceName, manageEventSource: true, restrictedToMinimumLevel: LogEventLevel.Warning);
    }
});

builder.Services.AddWRAppertureHostCore();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FailedAuthLimiter>();
builder.Services.AddSingleton<IconCache>();
builder.Services.AddSingleton<AgentIdentity>();
builder.Services.AddSingleton<AgentCertificateManager>();
builder.Services.AddSingleton<SessionWatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionWatcher>());

// Runs only with the real Kestrel server (never under TestServer), so tests don't touch the cert store.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    var services = kestrel.ApplicationServices;
    var port = services.GetRequiredService<IAgentSettingsStore>().Load().Port;
    var certificate = services.GetRequiredService<AgentCertificateManager>().EnsureCertificate();
    kestrel.ListenAnyIP(port, listen => listen.UseHttps(certificate));
    kestrel.AddServerHeader = false;
});

try
{
    // Build() already resolves Kestrel options, which loads (or creates) the certificate.
    var app = builder.Build();
    app.UseMiddleware<TokenAuthMiddleware>();
    app.MapAgentApi();
    app.MapSessionApi();

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        if (app.Environment.IsEnvironment(TestingEnvironment))
        {
            return;
        }

        var settings = app.Services.GetRequiredService<IAgentSettingsStore>().Load();
        var identity = app.Services.GetRequiredService<AgentIdentity>();
        Log.Information("Agent listening on https://*:{Port} (paired: {Paired}, fingerprint {Fingerprint})",
            settings.Port, settings.IsPaired, CertificateFingerprint.Format(identity.Fingerprint));
        if (!settings.IsPaired)
        {
            Log.Warning("Agent is not paired: run 'wrahost agent token' as administrator");
        }
    });

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    var hint = ex is UnauthorizedAccessException or System.Security.Cryptography.CryptographicException
        ? " (run as administrator or as the LocalSystem service)"
        : string.Empty;
    Log.Fatal(ex, "Agent failed to start{Hint}", hint);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point marker for WebApplicationFactory in tests.</summary>
public partial class Program;
