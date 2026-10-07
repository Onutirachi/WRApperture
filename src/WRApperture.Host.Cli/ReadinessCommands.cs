using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Models;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Cli;

internal static class ReadinessCommands
{
    public static IEnumerable<Command> Create(IServiceProvider services)
    {
        var json = new Option<bool>("--json") { Description = Strings.T("cli.options.json") };
        var check = new Command("check", Strings.T("cli.commands.check")) { json };
        check.SetAction(r => ExitCodes.Run(() =>
            Print(services.GetRequiredService<IHostPreparation>().Check(), r.GetValue(json))));
        yield return check;

        var prepare = new Command("prepare", Strings.T("cli.commands.prepare"));
        prepare.SetAction(_ => ExitCodes.Run(() =>
        {
            Print(services.GetRequiredService<IHostPreparation>().Prepare(), json: false);
            Output.Warn(Strings.T("cli.output.readiness.singleSessionReminder"));
        }));
        yield return prepare;

        var revert = new Command("revert", Strings.T("cli.commands.revert"));
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
            [
                Strings.T("cli.output.readiness.state"), Strings.T("cli.output.readiness.item"),
                Strings.T("cli.output.readiness.current"), Strings.T("cli.output.readiness.expected"),
            ],
            checks.Select(c => new[]
            {
                c.State switch
                {
                    ReadinessState.Ok => "OK",
                    ReadinessState.Pending => Strings.T("cli.output.readiness.pendingState"),
                    _ => Strings.T("cli.output.readiness.notSupportedState"),
                },
                c.Description, c.CurrentValue, c.ExpectedValue,
            }));

        var pending = checks.Count(c => c.State == ReadinessState.Pending);
        Console.WriteLine();
        Console.WriteLine(pending == 0 ? Strings.T("cli.output.readiness.ready") : Strings.T("cli.output.readiness.pending", pending));
    }
}
