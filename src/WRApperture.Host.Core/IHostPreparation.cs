using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Core;

public interface IHostPreparation
{
    /// <summary>Read-only diagnosis. Never requires admin.</summary>
    IReadOnlyList<ReadinessCheck> Check();

    /// <summary>
    /// Fixes the pending Remote Desktop settings (RDP, its policy and service, NLA, allow-list, firewall, language
    /// bar), recording previous values. Never touches the network category, groups or passwords. Idempotent.
    /// Requires admin.
    /// </summary>
    IReadOnlyList<ReadinessCheck> Prepare();

    /// <summary>
    /// Fixes one check whose <see cref="ReadinessCheck.Fix"/> is <see cref="ReadinessFix.Automatic"/>, recording
    /// previous values for <see cref="Revert"/>. Idempotent. Requires admin.
    /// </summary>
    IReadOnlyList<ReadinessCheck> Fix(ReadinessCheckId id);

    /// <summary>Gives the console user's local account a password (cannot be reverted). Requires admin.</summary>
    IReadOnlyList<ReadinessCheck> SetAccountPassword(string password);

    /// <summary>Restores values recorded by <see cref="Prepare"/> and <see cref="Fix"/>. Requires admin.</summary>
    IReadOnlyList<ReadinessCheck> Revert();
}
