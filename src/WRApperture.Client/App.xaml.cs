using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WRApperture.Client.Core;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Credentials;
using WRApperture.Client.Core.Launch;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Signing;
using WRApperture.Client.Core.Storage;
using WRApperture.Client.Services;
using WRApperture.Client.ViewModels;
using WRApperture.Client.Views;
using Serilog;

namespace WRApperture.Client;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("App", "client")
            .WriteTo.File(System.IO.Path.Combine(ClientPaths.LogDirectory, "client-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        DispatcherUnhandledException += OnUnhandledException;

        if (e.Args.Length > 0 && e.Args[0] is SigningSetup.SetupArgument or SigningSetup.RemoveArgument)
        {
            Shutdown(RunElevatedSigningCommand(e.Args));
            return;
        }

        _services = new ServiceCollection()
            .AddLogging(b => b.AddSerilog(dispose: false))
            .AddSingleton(ClientPaths.Default)
            .AddSingleton<HostStore>()
            .AddSingleton<IconStore>()
            .AddSingleton<IProcessStarter, ProcessStarter>()
            .AddSingleton<IProcessRunner, ProcessRunner>()
            .AddSingleton<RdpSigning>()
            .AddSingleton<IRdpSigner>(sp => sp.GetRequiredService<RdpSigning>())
            .AddSingleton<ISigningSetup, SigningSetup>()
            .AddSingleton<RemoteAppLauncher>()
            .AddSingleton<ICredentialStore, WindowsCredentialStore>()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<Func<PairedHost, IAgentApi>>(_ => AgentClient.ForHost)
            .AddSingleton<HostSync>()
            .AddSingleton<IDialogService, DialogService>()
            .AddSingleton<MainViewModel>()
            .BuildServiceProvider();

        Log.Information("Client started");
        WRApperture.Ui.ThemeManager.Initialize(this);
        var window = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    /// <summary>Elevated, windowless mode used by the UAC re-launch (and by the client uninstaller).</summary>
    private static int RunElevatedSigningCommand(string[] args)
    {
        var signing = new RdpSigning(new ProcessRunner(), new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger).CreateLogger<RdpSigning>());
        try
        {
            if (args[0] == SigningSetup.RemoveArgument)
            {
                signing.Remove();
            }
            else if (args.Length > 1)
            {
                signing.Setup(args[1]);
            }
            else
            {
                return 2;
            }

            return 0;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or UnauthorizedAccessException
                                       or ArgumentException or System.Security.SecurityException or IOException)
        {
            Log.Error(ex, "Elevated signing command {Command} failed", args[0]);
            MessageBox.Show($"Não foi possível concluir a configuração: {ex.Message}", "WRApperture", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        MessageBox.Show(e.Exception.Message, "WRApperture", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
