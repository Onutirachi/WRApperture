using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Platform;
using WRApperture.Host.Core.Publishing;

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
        services.AddSingleton<IAppPublisher, AppPublisher>();
        return services;
    }
}
