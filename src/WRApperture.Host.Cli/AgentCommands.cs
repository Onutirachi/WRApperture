using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core.Agent;
using WRApperture.Shared;

namespace WRApperture.Host.Cli;

internal static class AgentCommands
{
    public static Command Create(IServiceProvider services)
    {
        var agent = new Command("agent", "Configuração do agente HTTP (pareamento com clientes)");

        var showJson = new Option<bool>("--json") { Description = "Saída em JSON (para scripts)" };
        var show = new Command("show", "Mostra porta, pareamento e fingerprint do certificado") { showJson };
        show.SetAction(r => ExitCodes.Run(() =>
        {
            var settings = services.GetRequiredService<IAgentSettingsStore>().Load();
            var fingerprint = FindFingerprint(settings.CertificateThumbprint);
            if (r.GetValue(showJson))
            {
                Output.WriteJson(new { settings.Port, settings.IsPaired, CertificateFingerprint = fingerprint });
                return;
            }

            Console.WriteLine($"Porta:        {settings.Port}");
            Console.WriteLine($"Pareado:      {(settings.IsPaired ? "sim" : "não (execute 'wrahost agent token')")}");
            Console.WriteLine($"Fingerprint:  {(fingerprint is null ? "(certificado criado na primeira execução do agente)" : CertificateFingerprint.Format(fingerprint))}");
        }));
        agent.Subcommands.Add(show);

        var tokenJson = new Option<bool>("--json") { Description = "Saída em JSON (para scripts)" };
        var token = new Command("token", "Gera um novo token de pareamento (o anterior deixa de valer)") { tokenJson };
        token.SetAction(r => ExitCodes.Run(() =>
        {
            var value = services.GetRequiredService<IAgentSettingsStore>().RegenerateToken();
            if (r.GetValue(tokenJson))
            {
                Output.WriteJson(new { Token = value });
                return;
            }

            Console.WriteLine("Novo token de pareamento (exibido apenas agora; informe-o no cliente):");
            Console.WriteLine();
            Console.WriteLine($"    {value}");
            Console.WriteLine();
            Output.Warn("Clientes que usavam o token anterior precisarão do novo token.");
        }));
        agent.Subcommands.Add(token);

        var portArg = new Argument<int>("porta") { Description = $"Porta TCP ({AgentSettings.MinPort}-{AgentSettings.MaxPort})" };
        var port = new Command("port", "Define a porta do agente") { portArg };
        port.SetAction(r => ExitCodes.Run(() =>
        {
            services.GetRequiredService<IAgentSettingsStore>().SetPort(r.GetValue(portArg));
            Console.WriteLine($"Porta definida para {r.GetValue(portArg)}. Execute o instalador do host novamente para atualizar o serviço e o firewall.");
        }));
        agent.Subcommands.Add(port);

        var reset = new Command("reset", "Apaga token, porta e certificado do agente (clientes precisarão parear de novo)");
        reset.SetAction(_ => ExitCodes.Run(() =>
        {
            var store = services.GetRequiredService<IAgentSettingsStore>();
            var thumbprint = store.Load().CertificateThumbprint;
            store.Reset();
            var removed = RemoveCertificate(thumbprint);
            Console.WriteLine($"Configurações do agente apagadas{(removed ? " e certificado removido" : string.Empty)}.");
        }));
        agent.Subcommands.Add(reset);

        return agent;
    }

    /// <summary>Raw SHA-256 fingerprint of the agent certificate, or null when it does not exist yet.</summary>
    private static string? FindFingerprint(string? thumbprint)
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
            return found.Count == 0 ? null : CertificateFingerprint.Compute(found[0]);
        }
        finally
        {
            foreach (var cert in found)
            {
                cert.Dispose();
            }
        }
    }

    private static bool RemoveCertificate(string? thumbprint)
    {
        if (thumbprint is null)
        {
            return false;
        }

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        foreach (var cert in found)
        {
            store.Remove(cert);
            cert.Dispose();
        }

        return found.Count > 0;
    }
}
