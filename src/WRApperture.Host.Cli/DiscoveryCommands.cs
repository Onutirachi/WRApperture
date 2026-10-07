using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Errors;

namespace WRApperture.Host.Cli;

internal static class DiscoveryCommands
{
    public static IEnumerable<Command> Create(IServiceProvider services)
    {
        var json = new Option<bool>("--json") { Description = "Saída em JSON" };
        var discover = new Command("discover", "Lista os apps instalados (Menu Iniciar) que podem ser publicados") { json };
        discover.SetAction(r => ExitCodes.Run(() =>
        {
            var apps = services.GetRequiredService<IAppDiscovery>().Discover();
            if (r.GetValue(json))
            {
                Output.WriteJson(apps);
                return;
            }

            Output.WriteTable(
                ["NOME", "EXECUTÁVEL", "ARGS", "PUBLICADO"],
                apps.Select(a => new[] { a.DisplayName, a.ExecutablePath, a.Arguments ?? string.Empty, a.PublishedAlias ?? string.Empty }));
            Console.WriteLine();
            Console.WriteLine($"{apps.Count} app(s). Publique com: wrahost publish --path \"<EXECUTÁVEL>\" --name \"<NOME>\"");
        }));
        yield return discover;

        var alias = new Argument<string>("alias") { Description = "Alias do app publicado" };
        var output = new Option<FileInfo>("--out") { Description = "Arquivo PNG de saída", Required = true };
        var size = new Option<int>("--size") { Description = "Tamanho em pixels (16-256)", DefaultValueFactory = _ => 48 };
        var icon = new Command("icon", "Exporta o ícone de um app publicado como PNG") { alias, output, size };
        icon.SetAction(r => ExitCodes.Run(() =>
        {
            var a = r.GetValue(alias)!;
            var app = services.GetRequiredService<IAppPublisher>().Get(a)
                      ?? throw new AppValidationException("alias", $"app '{a}' não está publicado.");
            var file = r.GetValue(output)!;
            File.WriteAllBytes(file.FullName, services.GetRequiredService<IIconProvider>().GetPng(app.IconPath, app.IconIndex, r.GetValue(size)));
            Console.WriteLine($"Ícone salvo em {file.FullName}");
        }));
        yield return icon;
    }
}
