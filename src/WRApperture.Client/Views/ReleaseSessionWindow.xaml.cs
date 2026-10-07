using System.Windows;
using WRApperture.Shared;

namespace WRApperture.Client.Views;

public partial class ReleaseSessionWindow : Window
{
    public ReleaseSessionWindow(string hostName, IReadOnlyList<BusySessionDto> sessions)
    {
        InitializeComponent();
        Heading.Text = $"{hostName} está em uso";
        SessionsText.Text = string.Join("\n", sessions.Select(s =>
            s.Action == SessionActions.Logoff && IsRemoteApps(s)
                ? $"• {s.User}: apps remotos abertos → serão fechados"
                : $"• {s.User}: sessão {s.State}{(s.IsConsole ? " na tela do PC" : string.Empty)} → " +
                  (s.Action == SessionActions.Logoff ? "será encerrada" : "será desconectada (os programas dele continuam abertos)")));

        var logoff = sessions.Where(s => s.Action == SessionActions.Logoff).ToList();
        LogoffWarning.Visibility = logoff.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (logoff.Count > 0 && logoff.All(IsRemoteApps))
        {
            // Opening the full desktop: what closes are the apps already open from the clients.
            LogoffWarningText.Text ="Para abrir a área de trabalho, os apps remotos abertos serão fechados. O que não foi salvo neles será perdido.";
        }
    }

    private static bool IsRemoteApps(BusySessionDto session) => session.Kind == SessionModes.RemoteApp;

    public bool Always => AlwaysBox.IsChecked == true;

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
