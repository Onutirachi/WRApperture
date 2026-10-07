using System.Windows;
using WRApperture.Shared.Localization;

namespace WRApperture.Ui;

/// <summary>
/// "Automatic (Windows language)" or one of the shipped languages. The list comes from the translation
/// folders, so a new language shows up here without code changes. Applies on the next start.
/// </summary>
public partial class LanguageWindow : Window
{
    private sealed record Choice(string? Code, string Name);

    private LanguageWindow()
    {
        InitializeComponent();
        var choices = new List<Choice>
        {
            new(null, Strings.T("common.language.automatic", Strings.NativeName(Strings.WindowsLanguage.Name))),
        };
        choices.AddRange(Strings.LanguageChoices().Select(l => new Choice(l.Code, l.Name)));
        Choices.ItemsSource = choices;

        var current = Strings.PreferredLanguage;
        Choices.SelectedItem = choices.FirstOrDefault(c => string.Equals(c.Code, current, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        Loaded += (_, _) => Choices.Focus();
    }

    /// <summary>
    /// Lets the user pick a language; when it changed, offers to restart now (<paramref name="restart"/>).
    /// </summary>
    public static void Choose(Window? owner, Action restart)
    {
        var window = new LanguageWindow();
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowInTaskbar = true;
        }

        var before = Strings.PreferredLanguage;
        if (window.ShowDialog() != true || window.Choices.SelectedItem is not Choice choice ||
            string.Equals(choice.Code, before, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Strings.PreferredLanguage = choice.Code;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            MessageBox.Show(ex.Message, "WRApperture", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Shown in the chosen language, so the user sees right away that the choice worked.
        var chain = Strings.ChainFor(choice.Code ?? Strings.WindowsLanguage.Name);
        if (MessageBox.Show(Strings.Lookup("common.language.restart", chain), "WRApperture",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            restart();
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
