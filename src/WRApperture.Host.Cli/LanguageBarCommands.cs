using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using WRApperture.Host.Core.Readiness;

namespace WRApperture.Host.Cli;

/// <summary>Per-user language bar setting; no admin needed (acts on the current user's own profile).</summary>
internal static class LanguageBarCommands
{
    private const string Reconnect = "Vale a partir da próxima sessão: faça logoff da sessão remota (ou do Windows) e abra o app de novo.";

    public static Command Create(IServiceProvider services)
    {
        var langbar = new Command("langbar", "Barra de idioma flutuante nas sessões RemoteApp (usuário atual)");

        var hide = new Command("hide", "Oculta a barra de idioma para o usuário atual");
        hide.SetAction(_ => ExitCodes.Run(() =>
        {
            var settings = services.GetRequiredService<ILanguageBarSettings>();
            settings.Hide(settings.CurrentUserSid);
            Console.WriteLine("Barra de idioma oculta. " + Reconnect);
        }));
        langbar.Subcommands.Add(hide);

        var show = new Command("show", "Restaura o comportamento padrão do Windows para o usuário atual");
        show.SetAction(_ => ExitCodes.Run(() =>
        {
            var settings = services.GetRequiredService<ILanguageBarSettings>();
            settings.Show(settings.CurrentUserSid);
            Console.WriteLine("Barra de idioma restaurada ao padrão. " + Reconnect);
        }));
        langbar.Subcommands.Add(show);

        var status = new Command("status", "Mostra se a barra está oculta para o usuário atual");
        status.SetAction(_ => ExitCodes.Run(() =>
        {
            var settings = services.GetRequiredService<ILanguageBarSettings>();
            Console.WriteLine(settings.IsHidden(settings.CurrentUserSid) ? "Oculta" : "Padrão do Windows (pode aparecer flutuando nos apps remotos)");
        }));
        langbar.Subcommands.Add(status);

        return langbar;
    }
}
