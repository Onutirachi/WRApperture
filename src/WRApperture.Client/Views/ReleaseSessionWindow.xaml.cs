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
            $"• {s.User}: sessão {s.State}{(s.IsConsole ? " na tela do PC" : string.Empty)} → " +
            (s.Action == SessionActions.Logoff ? "será encerrada" : "será desconectada (os programas dele continuam abertos)")));
        LogoffWarning.Visibility = sessions.Any(s => s.Action == SessionActions.Logoff) ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool Always => AlwaysBox.IsChecked == true;

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
