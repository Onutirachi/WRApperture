using System.CommandLine;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Cli;
using WRApperture.Host.Core;
using Serilog;

Console.OutputEncoding = Encoding.UTF8;

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

    var root = new RootCommand("wrahost — publica apps do Windows como RemoteApp");
    foreach (var command in PublishCommands.Create(services))
    {
        root.Subcommands.Add(command);
    }

    return root.Parse(args).Invoke();
}
finally
{
    Log.CloseAndFlush();
}
