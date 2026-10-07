using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Agent;
using WRApperture.Host.Core.HostInfo;
using WRApperture.Host.Core.Discovery;
using WRApperture.Host.Core.Icons;
using WRApperture.Host.Core.Platform;
using WRApperture.Host.Core.Publishing;
using WRApperture.Host.Core.Readiness;

namespace WRApperture.Host.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the real Windows implementations of the Host Core. Requires logging to be registered.</summary>
    public static IServiceCollection AddWRAppertureHostCore(this IServiceCollection services)
    {
        services.AddSingleton<IRegistryAccess, WindowsRegistryAccess>();
        services.AddSingleton<IPrivilegeChecker, WindowsPrivilegeChecker>();
        services.AddSingleton<IFileSystemProbe, FileSystemProbe>();
        services.AddSingleton<IFirewallRules, ComFirewallRules>();
        services.AddSingleton<IStartMenuShortcuts, StartMenuShortcuts>();
        services.AddSingleton<IShortcutResolver, ShortcutResolver>();

        services.AddSingleton<IAppPublisher, AppPublisher>();
        services.AddSingleton<IHostPreparation, HostPreparation>();
        services.AddSingleton<IAppDiscovery, AppDiscovery>();
        services.AddSingleton<IIconProvider, IconProvider>();
        services.AddSingleton<IAgentSettingsStore, AgentSettingsStore>();
        services.AddSingleton<IHostInfoProvider, HostInfoProvider>();
        return services;
    }
}
