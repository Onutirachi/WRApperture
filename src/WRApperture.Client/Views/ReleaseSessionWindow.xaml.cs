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
            $"• {s.User}: sessão {s.State}{(s.IsConsole ? " na tela do PC" : string.Empty)}"));
    }

    public bool Always => AlwaysBox.IsChecked == true;

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
