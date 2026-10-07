using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Core;

public interface IHostPreparation
{
    /// <summary>Read-only diagnosis. Never requires admin.</summary>
    IReadOnlyList<ReadinessCheck> Check();

    /// <summary>Fixes pending checks, recording previous values. Idempotent. Requires admin.</summary>
    IReadOnlyList<ReadinessCheck> Prepare();

    /// <summary>Restores values recorded by <see cref="Prepare"/>. Requires admin.</summary>
    IReadOnlyList<ReadinessCheck> Revert();
}
