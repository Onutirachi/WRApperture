namespace WRApperture.Host.Core.Abstractions;

/// <summary>Windows services. The start type lives in the registry (HKLM\SYSTEM\…\Services\&lt;name&gt;\Start).</summary>
public interface IServiceControl
{
    /// <summary>False also when the service does not exist.</summary>
    bool IsRunning(string name);

    /// <summary>Starts the service and waits for it. Requires admin.</summary>
    void Start(string name);
}
