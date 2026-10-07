using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WRApperture.Host.Core;
using WRApperture.Host.Manager.Services;
using WRApperture.Host.Manager.ViewModels;
using WRApperture.Host.Manager.Views;

namespace WRApperture.Host.Manager;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WRApperture", "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("App", "manager")
            .WriteTo.File(Path.Combine(logDir, "manager-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        DispatcherUnhandledException += OnUnhandledException;

        // Writing to the registry, firewall and service needs admin: ask once; if declined, open read-only.
        var isAdmin = Elevation.IsAdministrator();
        if (!isAdmin && !e.Args.Contains(Elevation.ReadOnlyArgument) && Elevation.TryRelaunchElevated())
        {
            Shutdown();
            return;
        }

        _services = new ServiceCollection()
            .AddLogging(b => b.AddSerilog(dispose: false))
            .AddWRAppertureHostCore()
            .AddSingleton<IAgentService, AgentService>()
            .AddSingleton<IManagerDialogs, ManagerDialogs>()
            .AddSingleton<WRApperture.Shared.Updates.IUpdateChecker>(_ => WRApperture.Shared.Updates.UpdateChecker.CreateDefault())
            .AddSingleton(sp => ActivatorUtilities.CreateInstance<ManagerViewModel>(sp, isAdmin))
            .BuildServiceProvider();

        Log.Information("Host manager started (admin: {Admin})", isAdmin);
        WRApperture.Ui.ThemeManager.Initialize(this);
        var window = new MainWindow { DataContext = _services.GetRequiredService<ManagerViewModel>() };
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
