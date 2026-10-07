using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Publishing;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Cli;

internal static class DiscoveryCommands
{
    public static IEnumerable<Command> Create(IServiceProvider services)
    {
        var json = new Option<bool>("--json") { Description = Strings.T("cli.options.json") };
        var discover = new Command("discover", Strings.T("cli.commands.discover")) { json };
        discover.SetAction(r => ExitCodes.Run(() =>
        {
            var apps = services.GetRequiredService<IAppDiscovery>().Discover();
            if (r.GetValue(json))
            {
                Output.WriteJson(apps);
                return;
            }

            Output.WriteTable(
                [Strings.T("cli.output.discover.name"), Strings.T("cli.output.discover.executable"), "ARGS", Strings.T("cli.output.discover.published")],
                apps.Select(a => new[] { a.DisplayName, a.ExecutablePath, a.Arguments ?? string.Empty, a.PublishedAlias ?? string.Empty }));
            Console.WriteLine();
            Console.WriteLine(Strings.T("cli.output.discover.summary", apps.Count));
        }));
        yield return discover;

        var source = new Argument<string>(Strings.T("cli.arguments.file")) { Description = Strings.T("cli.arguments.fileDescription") };
        var inspectJson = new Option<bool>("--json") { Description = Strings.T("cli.options.json") };
        var inspect = new Command("inspect", Strings.T("cli.commands.inspect")) { source, inspectJson };
        inspect.SetAction(r => ExitCodes.Run(() =>
        {
            var draft = services.GetRequiredService<IAppSourceInspector>().Inspect(r.GetValue(source)!);
            if (r.GetValue(inspectJson))
            {
                Output.WriteJson(draft);
                return;
            }

            Output.WriteFields(
                (Strings.T("cli.output.inspect.name"), draft.DisplayName),
                (Strings.T("cli.output.inspect.program"), draft.ExecutablePath),
                (Strings.T("cli.output.inspect.arguments"), draft.Arguments ?? Strings.T("cli.output.inspect.noArguments")),
                (Strings.T("cli.output.inspect.icon"), $"{draft.IconPath},{draft.IconIndex}"));
        }));
        yield return inspect;

        var alias = new Argument<string>("alias") { Description = Strings.T("cli.arguments.publishedAlias") };
        var output = new Option<FileInfo>("--out") { Description = Strings.T("cli.options.out"), Required = true };
        var size = new Option<int>("--size") { Description = Strings.T("cli.options.size"), DefaultValueFactory = _ => 48 };
        var icon = new Command("icon", Strings.T("cli.commands.icon")) { alias, output, size };
        icon.SetAction(r => ExitCodes.Run(() =>
        {
            var a = r.GetValue(alias)!;
            var app = services.GetRequiredService<IAppPublisher>().Get(a)
                      ?? throw new AppValidationException("alias", Strings.T("cli.errors.notPublished", a));
            var file = r.GetValue(output)!;
            File.WriteAllBytes(file.FullName, services.GetRequiredService<IIconProvider>().GetPng(app.IconPath, app.IconIndex, r.GetValue(size)));
            Console.WriteLine(Strings.T("cli.output.icon.saved", file.FullName));
        }));
        yield return icon;
    }
}
