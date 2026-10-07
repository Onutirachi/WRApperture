using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core.Sessions;

namespace WRApperture.Host.Cli;

internal static class SessionCommands
{
    public static Command Create(IServiceProvider services)
    {
        var json = new Option<bool>("--json") { Description = "Saída em JSON" };
        var sessions = new Command("sessions", "Lista as sessões de usuário do host (diagnóstico, somente leitura)") { json };
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
                Console.WriteLine("Nenhuma sessão de usuário.");
                return;
            }

            Output.WriteTable(
                ["ID", "USUÁRIO", "ESTADO", "TIPO", "TELA DO HOST"],
                list.Select(s => new[]
                {
                    s.Id.ToString(), $@"{s.Domain}\{s.User}", s.State.ToString(),
                    s.Kind switch { SessionKind.Desktop => "área de trabalho", SessionKind.RemoteApp => "RemoteApp", _ => "-" },
                    s.IsConsole ? "sim" : string.Empty,
                }));
        }));
        return sessions;
    }
}
