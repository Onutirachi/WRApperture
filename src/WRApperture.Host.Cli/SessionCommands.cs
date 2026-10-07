using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core.Sessions;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Cli;

internal static class SessionCommands
{
    public static Command Create(IServiceProvider services)
    {
        var json = new Option<bool>("--json") { Description = Strings.T("cli.options.json") };
        var sessions = new Command("sessions", Strings.T("cli.commands.sessions")) { json };
        sessions.SetAction(r => ExitCodes.Run(() =>
        {
            var list = services.GetRequiredService<ISessionProvider>().List();
            if (r.GetValue(json))
            {
                Output.WriteJson(list);
                return;
            }

            if (list.Count == 0)
            {
                Console.WriteLine(Strings.T("cli.output.sessions.empty"));
                return;
            }

            Output.WriteTable(
                [
                    "ID", Strings.T("cli.output.sessions.user"), Strings.T("cli.output.sessions.state"),
                    Strings.T("cli.output.sessions.kind"), Strings.T("cli.output.sessions.hostScreen"),
                ],
                list.Select(s => new[]
                {
                    s.Id.ToString(), $@"{s.Domain}\{s.User}", s.State.ToString(),
                    s.Kind switch { SessionKind.Desktop => Strings.T("cli.output.sessions.desktop"), SessionKind.RemoteApp => "RemoteApp", _ => "-" },
                    s.IsConsole ? Strings.T("cli.output.yes") : string.Empty,
                }));
        }));
        return sessions;
    }
}
