using System.Windows;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Client.Views;

public partial class ReleaseSessionWindow : Window
{
    public ReleaseSessionWindow(string hostName, IReadOnlyList<BusySessionDto> sessions)
    {
        InitializeComponent();
        Heading.Text = Strings.T("client.release.heading", hostName);
        SessionsText.Text = string.Join("\n", sessions.Select(Describe));

        var logoff = sessions.Where(s => s.Action == SessionActions.Logoff).ToList();
        LogoffWarning.Visibility = logoff.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (logoff.Count > 0 && logoff.All(IsRemoteApps))
        {
            // Opening the full desktop: what closes are the apps already open from the clients.
            LogoffWarningText.Text = Strings.T("client.release.desktopWarning");
        }
    }

    /// <summary>One line per session: who, and what happens to it.</summary>
    private static string Describe(BusySessionDto s)
    {
        if (s.Action == SessionActions.Logoff && IsRemoteApps(s))
        {
            return Strings.T("client.release.remoteAppsLine", s.User);
        }

        var outcome = s.Action == SessionActions.Logoff ? Strings.T("client.release.willLogoff") : Strings.T("client.release.willDisconnect");
        var state = State(s.State);
        return s.IsConsole
            ? Strings.T("client.release.consoleSessionLine", s.User, state, outcome)
            : Strings.T("client.release.sessionLine", s.User, state, outcome);
    }

    /// <summary>Agents send a code (0.8.0+) or, before that, Portuguese text; anything else is shown as is.</summary>
    private static string State(string state) => state switch
    {
        SessionStates.Active or "em uso" => Strings.T("client.release.stateActive"),
        SessionStates.Disconnected or "desconectada" => Strings.T("client.release.stateDisconnected"),
        _ => state,
    };

    private static bool IsRemoteApps(BusySessionDto session) => session.Kind == SessionModes.RemoteApp;

    public bool Always => AlwaysBox.IsChecked == true;

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
