namespace WRApperture.Host.Core.Abstractions;

/// <summary>Power options of the active power plan.</summary>
public interface IPowerSettings
{
    /// <summary>Idle time before sleeping when plugged in; null when the PC never sleeps (or it is unknown).</summary>
    TimeSpan? SleepAfter();
}
