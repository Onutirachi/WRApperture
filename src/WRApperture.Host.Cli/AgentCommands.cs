using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core.Agent;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Cli;

internal static class AgentCommands
{
    public static Command Create(IServiceProvider services)
    {
        var agent = new Command("agent", Strings.T("cli.commands.agent"));

        var showJson = new Option<bool>("--json") { Description = Strings.T("cli.options.jsonForScripts") };
        var show = new Command("show", Strings.T("cli.commands.agentShow")) { showJson };
        show.SetAction(r => ExitCodes.Run(() =>
        {
            var settings = services.GetRequiredService<IAgentSettingsStore>().Load();
            var fingerprint = FindFingerprint(settings.CertificateThumbprint);
            if (r.GetValue(showJson))
            {
                Output.WriteJson(new { settings.Port, settings.IsPaired, CertificateFingerprint = fingerprint });
                return;
            }

            Output.WriteFields(
                (Strings.T("cli.output.agent.port"), settings.Port.ToString()),
                (Strings.T("cli.output.agent.paired"), settings.IsPaired ? Strings.T("cli.output.yes") : Strings.T("cli.output.agent.notPaired")),
                (Strings.T("cli.output.agent.fingerprint"),
                    fingerprint is null ? Strings.T("cli.output.agent.noCertificate") : CertificateFingerprint.Format(fingerprint)));
        }));
        agent.Subcommands.Add(show);

        var tokenJson = new Option<bool>("--json") { Description = Strings.T("cli.options.jsonForScripts") };
        var token = new Command("token", Strings.T("cli.commands.agentToken")) { tokenJson };
        token.SetAction(r => ExitCodes.Run(() =>
        {
            var value = services.GetRequiredService<IAgentSettingsStore>().RegenerateToken();
            if (r.GetValue(tokenJson))
            {
                Output.WriteJson(new { Token = value });
                return;
            }

            Console.WriteLine(Strings.T("cli.output.agent.newToken"));
            Console.WriteLine();
            Console.WriteLine($"    {value}");
            Console.WriteLine();
            Output.Warn(Strings.T("cli.output.agent.previousTokenInvalid"));
        }));
        agent.Subcommands.Add(token);

        var portArg = new Argument<int>(Strings.T("cli.arguments.port"))
        {
            Description = Strings.T("cli.arguments.portDescription", AgentSettings.MinPort, AgentSettings.MaxPort),
        };
        var port = new Command("port", Strings.T("cli.commands.agentPort")) { portArg };
        port.SetAction(r => ExitCodes.Run(() =>
        {
            services.GetRequiredService<IAgentSettingsStore>().SetPort(r.GetValue(portArg));
            Console.WriteLine(Strings.T("cli.output.agent.portSet", r.GetValue(portArg)));
        }));
        agent.Subcommands.Add(port);

        var reset = new Command("reset", Strings.T("cli.commands.agentReset"));
        reset.SetAction(_ => ExitCodes.Run(() =>
        {
            var store = services.GetRequiredService<IAgentSettingsStore>();
            var thumbprint = store.Load().CertificateThumbprint;
            store.Reset();
            var removed = RemoveCertificate(thumbprint);
            Console.WriteLine(removed ? Strings.T("cli.output.agent.resetWithCertificate") : Strings.T("cli.output.agent.reset"));
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
