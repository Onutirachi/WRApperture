using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core.Readiness;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Cli;

/// <summary>Per-user language bar setting; no admin needed (acts on the current user's own profile).</summary>
internal static class LanguageBarCommands
{
    public static Command Create(IServiceProvider services)
    {
        var langbar = new Command("langbar", Strings.T("cli.commands.langbar"));

        var hide = new Command("hide", Strings.T("cli.commands.langbarHide"));
        hide.SetAction(_ => ExitCodes.Run(() =>
        {
            var settings = services.GetRequiredService<ILanguageBarSettings>();
            settings.Hide(settings.CurrentUserSid);
            Console.WriteLine(Strings.T("cli.output.langbar.hidden") + " " + Strings.T("cli.output.langbar.nextSession"));
        }));
        langbar.Subcommands.Add(hide);

        var show = new Command("show", Strings.T("cli.commands.langbarShow"));
        show.SetAction(_ => ExitCodes.Run(() =>
        {
            var settings = services.GetRequiredService<ILanguageBarSettings>();
            settings.Show(settings.CurrentUserSid);
            Console.WriteLine(Strings.T("cli.output.langbar.restored") + " " + Strings.T("cli.output.langbar.nextSession"));
        }));
        langbar.Subcommands.Add(show);

        var status = new Command("status", Strings.T("cli.commands.langbarStatus"));
        status.SetAction(_ => ExitCodes.Run(() =>
        {
            var settings = services.GetRequiredService<ILanguageBarSettings>();
            Console.WriteLine(settings.IsHidden(settings.CurrentUserSid)
                ? Strings.T("cli.output.langbar.statusHidden")
                : Strings.T("cli.output.langbar.statusDefault"));
        }));
        langbar.Subcommands.Add(status);

        return langbar;
    }
}
