using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Cli;

internal static class ReadinessCommands
{
    public static IEnumerable<Command> Create(IServiceProvider services)
    {
        var json = new Option<bool>("--json") { Description = "Saída em JSON" };
        var check = new Command("check", "Diagnostica se o host está pronto para RemoteApp") { json };
        check.SetAction(r => ExitCodes.Run(() =>
            Print(services.GetRequiredService<IHostPreparation>().Check(), r.GetValue(json))));
        yield return check;

        var prepare = new Command("prepare", "Habilita RDP, NLA, allow-list e firewall (idempotente)");
        prepare.SetAction(_ => ExitCodes.Run(() =>
        {
            Print(services.GetRequiredService<IHostPreparation>().Prepare(), json: false);
            Output.Warn("Lembrete: no Windows Pro só há uma sessão por vez. Abrir um RemoteApp desconecta quem estiver usando este PC.");
        }));
        yield return prepare;

        var revert = new Command("revert", "Desfaz as alterações feitas por 'prepare'");
        revert.SetAction(_ => ExitCodes.Run(() =>
            Print(services.GetRequiredService<IHostPreparation>().Revert(), json: false)));
        yield return revert;
    }

    private static void Print(IReadOnlyList<ReadinessCheck> checks, bool json)
    {
        if (json)
        {
            Output.WriteJson(checks);
            return;
        }

        Output.WriteTable(
            ["ESTADO", "ITEM", "ATUAL", "ESPERADO"],
            checks.Select(c => new[]
            {
                c.State switch
                {
                    ReadinessState.Ok => "OK",
                    ReadinessState.Pending => "PENDENTE",
                    _ => "NÃO SUPORTADO",
                },
                c.Description, c.CurrentValue, c.ExpectedValue,
            }));

        var pending = checks.Count(c => c.State == ReadinessState.Pending);
        Console.WriteLine();
        Console.WriteLine(pending == 0 ? "Host pronto para RemoteApp." : $"{pending} item(ns) pendente(s). Execute 'wrahost prepare' como administrador.");
    }
}
