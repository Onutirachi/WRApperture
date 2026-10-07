using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Models;
using WRApperture.Shared.Localization;

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
        var json = new Option<bool>("--json") { Description = Strings.T("cli.options.json") };
        var cmd = new Command("list", Strings.T("cli.commands.list")) { json };
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
                Console.WriteLine(Strings.T("cli.output.list.empty"));
                return;
            }

            Output.WriteTable(
                ["ALIAS", Strings.T("cli.output.list.name"), Strings.T("cli.output.list.path"), "ARGS", Strings.T("cli.output.list.managed")],
                apps.Select(a => new[]
                {
                    a.Alias, a.DisplayName, a.ExecutablePath,
                    a.CommandLinePolicy switch
                    {
                        CommandLinePolicy.Any => Strings.T("cli.output.list.anyArguments"),
                        CommandLinePolicy.Required => a.RequiredArguments ?? string.Empty,
                        _ => "-",
                    },
                    a.IsManaged ? Strings.T("cli.output.yes") : Strings.T("cli.output.no"),
                }));
        }));
        return cmd;
    }

    private static Command Publish(IServiceProvider services)
    {
        var path = new Option<string>("--path") { Description = Strings.T("cli.options.path"), Required = true };
        var name = new Option<string>("--name") { Description = Strings.T("cli.options.name"), Required = true };
        var alias = new Option<string?>("--alias") { Description = Strings.T("cli.options.alias") };
        var args = new Option<string?>("--args") { Description = Strings.T("cli.options.args") };
        var policy = new Option<string?>("--args-policy") { Description = "none | any | required" };
        policy.AcceptOnlyFromAmong("none", "any", "required");
        var icon = new Option<string?>("--icon") { Description = Strings.T("cli.options.icon") };
        var iconIndex = new Option<int>("--icon-index") { Description = Strings.T("cli.options.iconIndex"), DefaultValueFactory = _ => 0 };
        var hidden = new Option<bool>("--hidden") { Description = Strings.T("cli.options.hidden") };

        var cmd = new Command("publish", Strings.T("cli.commands.publish")) { path, name, alias, args, policy, icon, iconIndex, hidden };
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

            var app = result.App;
            Console.WriteLine(result.Outcome switch
            {
                PublishOutcome.Created => Strings.T("cli.output.publish.created", app.DisplayName, app.Alias),
                PublishOutcome.Updated => Strings.T("cli.output.publish.updated", app.DisplayName, app.Alias),
                _ => Strings.T("cli.output.publish.unchanged", app.DisplayName, app.Alias),
            });
            if (result.Warnings.Contains(PublishWarnings.WritableLocation))
            {
                Output.Warn(Strings.T("cli.output.publish.writableLocation"));
            }
        }));
        return cmd;
    }

    private static Command Unpublish(IServiceProvider services)
    {
        var alias = new Argument<string>("alias") { Description = Strings.T("cli.arguments.alias") };
        var cmd = new Command("unpublish", Strings.T("cli.commands.unpublish")) { alias };
        cmd.SetAction(r => ExitCodes.Run(() =>
        {
            var a = r.GetValue(alias)!;
            Console.WriteLine(services.GetRequiredService<IAppPublisher>().Unpublish(a)
                ? Strings.T("cli.output.unpublish.removed", a)
                : Strings.T("cli.output.unpublish.notPublished", a));
        }));
        return cmd;
    }
}
