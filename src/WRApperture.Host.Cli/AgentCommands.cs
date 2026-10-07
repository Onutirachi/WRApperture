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

        var show = new Command("show", "Mostra porta, pareamento e fingerprint do certificado");
        show.SetAction(_ => ExitCodes.Run(() =>
        {
            var settings = services.GetRequiredService<IAgentSettingsStore>().Load();
            Console.WriteLine($"Porta:        {settings.Port}");
            Console.WriteLine($"Pareado:      {(settings.IsPaired ? "sim" : "não (execute 'wrahost agent token')")}");
            Console.WriteLine($"Fingerprint:  {FindFingerprint(settings.CertificateThumbprint) ?? "(certificado criado na primeira execução do agente)"}");
        }));
        agent.Subcommands.Add(show);

        var token = new Command("token", "Gera um novo token de pareamento (o anterior deixa de valer)");
        token.SetAction(_ => ExitCodes.Run(() =>
        {
            var value = services.GetRequiredService<IAgentSettingsStore>().RegenerateToken();
            Console.WriteLine("Novo token de pareamento (exibido apenas agora; informe-o no cliente):");
            Console.WriteLine();
            Console.WriteLine($"    {value}");
            Console.WriteLine();
            Output.Warn("Clientes que usavam o token anterior precisarão do novo token.");
        }));
        agent.Subcommands.Add(token);

        var portArg = new Argument<int>("porta") { Description = $"Porta TCP ({AgentSettings.MinPort}-{AgentSettings.MaxPort})" };
        var port = new Command("port", "Define a porta do agente (reinicie o agente para aplicar)") { portArg };
        port.SetAction(r => ExitCodes.Run(() =>
        {
            services.GetRequiredService<IAgentSettingsStore>().SetPort(r.GetValue(portArg));
            Console.WriteLine($"Porta definida para {r.GetValue(portArg)}. Reinicie o agente e ajuste a regra de firewall.");
        }));
        agent.Subcommands.Add(port);

        return agent;
    }

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
            return found.Count == 0 ? null : CertificateFingerprint.Format(CertificateFingerprint.Compute(found[0]));
        }
        finally
        {
            foreach (var cert in found)
            {
                cert.Dispose();
            }
        }
    }
}
