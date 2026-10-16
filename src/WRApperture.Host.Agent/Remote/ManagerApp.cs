using WRApperture.Host.Agent.Pairing;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Publishing;

namespace WRApperture.Host.Agent.Remote;

/// <summary>
/// The host app as a RemoteApp, for the clients' "Manage host" button: published hidden from the apps list when it is
/// not published yet. When someone already published WRApperture.Host.exe by hand, that entry is used as is.
/// </summary>
internal sealed class ManagerApp(IAppPublisher publisher, IFileSystemProbe files, PairingOptions options, ILogger<ManagerApp> logger)
{
    private readonly object _gate = new();
    private bool _failed;

    /// <summary>The alias to open, or null when the host app is not installed (or could not be published).</summary>
    public string? Alias()
    {
        var path = options.ApproverPath;
        if (!files.FileExists(path))
        {
            return null;
        }

        lock (_gate)
        {
            var existing = publisher.List().FirstOrDefault(a =>
                string.Equals(a.ExecutablePath, path, StringComparison.OrdinalIgnoreCase) && a.CommandLinePolicy == CommandLinePolicy.None);
            if (existing is not null)
            {
                return existing.Alias;
            }

            if (_failed)
            {
                return null; // tried once since the agent started
            }

            try
            {
                var result = publisher.Publish(new PublishRequest(path, BuiltInApps.ManagerDisplayName, BuiltInApps.ManagerAlias, ShowInList: false));
                logger.LogInformation("Published the host app as RemoteApp {Alias}", result.App.Alias);
                return result.App.Alias;
            }
            catch (Exception ex) when (ex is AdminRequiredException or AppValidationException or UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                _failed = true;
                logger.LogWarning(ex, "Could not publish the host app");
                return null;
            }
        }
    }
}
