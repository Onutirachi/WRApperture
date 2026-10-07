using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Cli;

internal static class PublishCommands
{
    public static IEnumerable<Command> Create(IServiceProvider services)
    {
        yield return List(services);
        yield return Publish(services);
        yield return Unpublish(services);
    }

    private static Command List(IServiceProvider services)
    {
        var json = new Option<bool>("--json") { Description = "Saída em JSON" };
        var cmd = new Command("list", "Lista os apps publicados como RemoteApp") { json };
        cmd.SetAction(r => ExitCodes.Run(() =>
        {
            var apps = services.GetRequiredService<IAppPublisher>().List();
            if (r.GetValue(json))
            {
                Output.WriteJson(apps);
                return;
            }

            if (apps.Count == 0)
            {
                Console.WriteLine("Nenhum app publicado.");
                return;
            }

            Output.WriteTable(
                ["ALIAS", "NOME", "CAMINHO", "ARGS", "GERENCIADO"],
                apps.Select(a => new[]
                {
                    a.Alias, a.DisplayName, a.ExecutablePath,
                    a.CommandLinePolicy switch
                    {
                        CommandLinePolicy.Any => "qualquer",
                        CommandLinePolicy.Required => a.RequiredArguments ?? string.Empty,
                        _ => "-",
                    },
                    a.IsManaged ? "sim" : "não",
                }));
        }));
        return cmd;
    }

    private static Command Publish(IServiceProvider services)
    {
        var path = new Option<string>("--path") { Description = "Caminho do executável (aceita %VARIAVEIS%)", Required = true };
        var name = new Option<string>("--name") { Description = "Nome de exibição", Required = true };
        var alias = new Option<string?>("--alias") { Description = "Alias (gerado a partir do nome se omitido)" };
        var args = new Option<string?>("--args") { Description = "Argumentos fixos (implica --args-policy required)" };
        var policy = new Option<string?>("--args-policy") { Description = "none | any | required" };
        policy.AcceptOnlyFromAmong("none", "any", "required");
        var icon = new Option<string?>("--icon") { Description = "Arquivo do ícone (padrão: o próprio exe)" };
        var iconIndex = new Option<int>("--icon-index") { Description = "Índice do ícone", DefaultValueFactory = _ => 0 };
        var hidden = new Option<bool>("--hidden") { Description = "Não exibir em listagens" };

        var cmd = new Command("publish", "Publica (ou atualiza) um app como RemoteApp") { path, name, alias, args, policy, icon, iconIndex, hidden };
        cmd.SetAction(r => ExitCodes.Run(() =>
        {
            var fixedArgs = r.GetValue(args);
            var argsPolicy = r.GetValue(policy) switch
            {
                "any" => CommandLinePolicy.Any,
                "required" => CommandLinePolicy.Required,
                "none" => CommandLinePolicy.None,
                _ => string.IsNullOrEmpty(fixedArgs) ? CommandLinePolicy.None : CommandLinePolicy.Required,
            };

            var result = services.GetRequiredService<IAppPublisher>().Publish(new PublishRequest(
                r.GetValue(path)!,
                r.GetValue(name)!,
                r.GetValue(alias),
                r.GetValue(icon),
                r.GetValue(iconIndex),
                argsPolicy,
                fixedArgs,
                !r.GetValue(hidden)));

            var verb = result.Outcome switch
            {
                PublishOutcome.Created => "publicado",
                PublishOutcome.Updated => "atualizado",
                _ => "já estava publicado (sem alterações)",
            };
            Console.WriteLine($"'{result.App.DisplayName}' {verb}. Alias: {result.App.Alias}");
            if (result.Warnings.Contains(PublishWarnings.WritableLocation))
            {
                Output.Warn("Aviso: o executável está em uma pasta que usuários comuns podem alterar. Prefira instalar em Program Files.");
            }
        }));
        return cmd;
    }

    private static Command Unpublish(IServiceProvider services)
    {
        var alias = new Argument<string>("alias") { Description = "Alias do app" };
        var cmd = new Command("unpublish", "Remove a publicação de um app") { alias };
        cmd.SetAction(r => ExitCodes.Run(() =>
        {
            var a = r.GetValue(alias)!;
            Console.WriteLine(services.GetRequiredService<IAppPublisher>().Unpublish(a)
                ? $"'{a}' removido."
                : $"'{a}' não estava publicado.");
        }));
        return cmd;
    }
}
