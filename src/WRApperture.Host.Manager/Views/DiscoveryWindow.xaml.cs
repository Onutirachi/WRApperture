using System.Windows;
using System.Windows.Controls;
using WRApperture.Host.Core.Models;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Manager.Views;

public partial class DiscoveryWindow : Window
{
    private readonly List<Choice> _choices;

    public DiscoveryWindow(IReadOnlyList<DiscoveredApp> apps)
    {
        InitializeComponent();
        _choices = apps.Select(a => new Choice(a)).ToList();
        List.ItemsSource = _choices;
        UpdateCount();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public IReadOnlyList<DiscoveredApp> Selected { get; private set; } = [];

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var term = SearchBox.Text.Trim();
        List.ItemsSource = term.Length == 0
            ? _choices
            : _choices.Where(c => c.App.DisplayName.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void OnCheckedChanged(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        var count = _choices.Count(c => c.IsSelected);
        CountText.Text = count == 0 ? Strings.T("manager.discovery.found", _choices.Count) : Strings.T("manager.discovery.selected", count);
    }

    private void OnPublish(object sender, RoutedEventArgs e)
    {
        Selected = _choices.Where(c => c.IsSelected).Select(c => c.App).ToList();
        DialogResult = true;
    }

    public sealed class Choice(DiscoveredApp app)
    {
        public DiscoveredApp App { get; } = app;
        public bool IsSelected { get; set; }
        public bool CanSelect => App.PublishedAlias is null;
        public string Badge => App.PublishedAlias is null ? string.Empty : "  · " + Strings.T("manager.discovery.alreadyPublished");
    }
}
