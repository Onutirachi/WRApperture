using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WRApperture.Client.Core;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Credentials;
using WRApperture.Client.Core.Launch;
using WRApperture.Client.Core.Model;
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

        _services = new ServiceCollection()
            .AddLogging(b => b.AddSerilog(dispose: false))
            .AddSingleton(ClientPaths.Default)
            .AddSingleton<HostStore>()
            .AddSingleton<IconStore>()
            .AddSingleton<IProcessStarter, ProcessStarter>()
            .AddSingleton<RemoteAppLauncher>()
            .AddSingleton<ICredentialStore, WindowsCredentialStore>()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<Func<PairedHost, IAgentApi>>(_ => AgentClient.ForHost)
            .AddSingleton<HostSync>()
            .AddSingleton<IDialogService, DialogService>()
            .AddSingleton<MainViewModel>()
            .BuildServiceProvider();

        Log.Information("Client started");
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

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        MessageBox.Show(e.Exception.Message, "WRApperture", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
