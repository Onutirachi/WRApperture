using System.CommandLine;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Cli;
using WRApperture.Host.Core;
using WRApperture.Shared.Localization;
using Serilog;

Console.OutputEncoding = Encoding.UTF8;

// System.CommandLine's own texts (Usage, Options, parse errors) follow CurrentUICulture: keep them in our language
// when it was forced (WRAPPERTURE_LANG or the registry) to something other than the Windows display language.
var windowsLanguage = Strings.ChainFor(CultureInfo.CurrentUICulture.Name)
    .First(l => Strings.AvailableLanguages.Contains(l, StringComparer.OrdinalIgnoreCase));
if (!string.Equals(windowsLanguage, Strings.Language, StringComparison.OrdinalIgnoreCase))
{
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Strings.Chain[0]);
}

var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WRApperture", "logs");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.WithProperty("App", "wrahost")
    .WriteTo.File(Path.Combine(logDir, "wrahost-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();

try
{
    using var services = new ServiceCollection()
        .AddLogging(b => b.AddSerilog(dispose: false))
        .AddWRAppertureHostCore()
        .BuildServiceProvider();

    var root = new RootCommand(Strings.T("cli.commands.root"));
    var commands = PublishCommands.Create(services)
        .Concat(ReadinessCommands.Create(services))
        .Concat(DiscoveryCommands.Create(services));
    foreach (var command in commands)
    {
        root.Subcommands.Add(command);
    }

    root.Subcommands.Add(AgentCommands.Create(services));
    root.Subcommands.Add(LanguageBarCommands.Create(services));
    root.Subcommands.Add(SessionCommands.Create(services));

    return root.Parse(args).Invoke();
}
finally
{
    Log.CloseAndFlush();
}
