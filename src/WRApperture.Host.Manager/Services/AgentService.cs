using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Manager.Services;

public sealed record AgentStatus(bool Installed, bool Running, string Text);

public interface IAgentService
{
    AgentStatus GetStatus();

    /// <summary>Starts the agent, or restarts it when running (admin).</summary>
    void Restart();

    IReadOnlyList<string> Ipv4Addresses();

    /// <summary>Formatted SHA-256 fingerprint of the agent certificate, or null before the agent's first start.</summary>
    string? Fingerprint(string? thumbprint);
}

internal sealed class AgentService : IAgentService
{
    public const string ServiceName = "WRAppertureAgent";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public AgentStatus GetStatus()
    {
        using var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == ServiceName);
        if (service is null)
        {
            return new AgentStatus(false, false, Strings.T("manager.agent.notInstalled"));
        }

        return service.Status switch
        {
            ServiceControllerStatus.Running => new AgentStatus(true, true, Strings.T("manager.agent.running")),
            ServiceControllerStatus.StartPending => new AgentStatus(true, false, Strings.T("manager.agent.starting")),
            ServiceControllerStatus.StopPending => new AgentStatus(true, false, Strings.T("manager.agent.stopping")),
            _ => new AgentStatus(true, false, Strings.T("manager.agent.stopped")),
        };
    }

    public void Restart()
    {
        using var service = new ServiceController(ServiceName);
        if (service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
        }

        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, Timeout);
    }

    public IReadOnlyList<string> Ipv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(a => a.Address.ToString())
            .Distinct()
            .ToList();

    public string? Fingerprint(string? thumbprint)
    {
        if (thumbprint is null)
        {
            return null;
        }

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        try
        {
            return found.Count == 0 ? null : CertificateFingerprint.Format(CertificateFingerprint.Compute(found[0]));
        }
        finally
        {
            foreach (var certificate in found)
            {
                certificate.Dispose();
            }
        }
    }
}
