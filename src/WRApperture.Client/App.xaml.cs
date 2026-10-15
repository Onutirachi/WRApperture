using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WRApperture.Client.Core;
using WRApperture.Client.Core.Agent;
using WRApperture.Client.Core.Credentials;
using WRApperture.Client.Core.Discovery;
using WRApperture.Client.Core.Launch;
using WRApperture.Client.Core.Model;
using WRApperture.Client.Core.Signing;
using WRApperture.Client.Core.Startup;
using WRApperture.Client.Core.Storage;
using WRApperture.Client.Core.Updates;
using WRApperture.Client.Services;
using WRApperture.Client.ViewModels;
using WRApperture.Client.Views;
using WRApperture.Shared.Localization;
using WRApperture.Shared.Updates;
using Serilog;

namespace WRApperture.Client;

public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstance? _instance;
    private TrayIcon? _tray;

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

        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            SingleInstance.SignalRunningInstance();
            Shutdown();
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
            .AddSingleton<IUpdateChecker>(_ => UpdateChecker.CreateDefault())
            .AddSingleton<ClientUpdates>()
            .AddSingleton<IHostDiscovery>(sp => new HostDiscovery(logger: sp.GetRequiredService<ILogger<HostDiscovery>>()))
            .AddSingleton<IDialogService, DialogService>()
            .AddSingleton(new StartupRegistration())
            .AddSingleton<MainViewModel>()
            .BuildServiceProvider();

        var startHidden = e.Args.Contains(StartupRegistration.TrayArgument);
        Log.Information("Client started (tray: {Tray})", startHidden);
        WRApperture.Ui.ThemeManager.Initialize(this);
        RefreshStartupEntry();

        // The window hides to the tray instead of closing, so the app ends only through Exit.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var vm = _services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        _tray = new TrayIcon(vm, ShowMainWindow, ExitApp);
        window.HiddenToTray += () =>
        {
            if (vm.ConsumeTrayHint())
            {
                _tray.ShowNotice("WRApperture", Strings.T("client.tray.hint"));
            }
        };
        _instance.ShowRequested += () => Dispatcher.BeginInvoke(ShowMainWindow);
        vm.NoticeRequested += (text, onClick) => _tray.ShowNotice("WRApperture", text, onClick);
        vm.ExitRequested += ExitApp;
        vm.StartUpdateChecks();

        if (!startHidden)
        {
            window.Show();
        }
    }

    private void ShowMainWindow()
    {
        if (MainWindow is not { } window)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    /// <summary>Starts a fresh client (e.g. after changing the language) and quits this one.</summary>
    public void Restart()
    {
        // Let go of the single-instance lock first, or the new process would just wake this one up.
        _instance?.Dispose();
        _instance = null;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        ExitApp();
    }

    private void ExitApp()
    {
        if (MainWindow is Views.MainWindow window)
        {
            window.CloseForExit();
        }
        else
        {
            Shutdown();
        }
    }

    /// <summary>Keeps "start with Windows" pointing at this exe if it was enabled from another install path.</summary>
    private void RefreshStartupEntry()
    {
        try
        {
            _services!.GetRequiredService<StartupRegistration>().Refresh(Environment.ProcessPath!);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warning(ex, "Could not refresh the Windows startup entry");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
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
            MessageBox.Show(Strings.T("client.signing.setupFailed", ex.Message), "WRApperture", MessageBoxButton.OK, MessageBoxImage.Error);
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
