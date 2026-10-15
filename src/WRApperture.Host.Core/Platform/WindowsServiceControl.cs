using System.ServiceProcess;
using WRApperture.Host.Core.Abstractions;

namespace WRApperture.Host.Core.Platform;

internal sealed class WindowsServiceControl : IServiceControl
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    public bool IsRunning(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            return service.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false; // not installed
        }
    }

    public void Start(string name)
    {
        using var service = new ServiceController(name);
        if (service.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
        {
            service.Start();
        }

        service.WaitForStatus(ServiceControllerStatus.Running, StartTimeout);
    }
}
