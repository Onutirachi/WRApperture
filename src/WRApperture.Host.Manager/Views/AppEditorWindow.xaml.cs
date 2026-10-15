using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.StartWith;
using WRApperture.Host.Manager.Services;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Manager.Views;

/// <summary>Review/edit an app before publishing. Saving goes through the caller's callback so Core validation errors show inline.</summary>
public partial class AppEditorWindow : Window
{
    private readonly EditorInput _input;
    private readonly Func<PublishRequest, string?> _trySave;
    private readonly Func<string, int, ImageSource?> _loadIcon;
    private readonly Func<string, IReadOnlyList<StartWithProgram>> _detectStartWith;
    private string _iconPath;
    private int _iconIndex;

    public AppEditorWindow(EditorInput input, Func<PublishRequest, string?> trySave, Func<string, int, ImageSource?> loadIcon,
        Func<string, IReadOnlyList<StartWithProgram>> detectStartWith)
    {
        InitializeComponent();
        _input = input;
        _trySave = trySave;
        _loadIcon = loadIcon;
        _detectStartWith = detectStartWith;
        _iconPath = input.IconPath;
        _iconIndex = input.IconIndex;

        Title = input.Title;
        Heading.Text = input.Title;
        AliasText.Text = input.Alias is null ? Strings.T("manager.editor.aliasPending") : Strings.T("manager.editor.alias", input.Alias);
        SaveButton.Content = Strings.T(input.Alias is null ? "manager.editor.publish" : "manager.editor.save");
        NameBox.Text = input.DisplayName;
        PathBox.Text = input.ExecutablePath;
        ArgsBox.Text = input.Arguments ?? string.Empty;
        ShowBox.IsChecked = input.ShowInList;
        (input.Policy switch
        {
            CommandLinePolicy.Required => FixedArgs,
            CommandLinePolicy.Any => AnyArgs,
            _ => NoArgs,
        }).IsChecked = true;
        IconPreview.Source = loadIcon(_iconPath, _iconIndex);
        AutoStartBox.IsChecked = input.AutoStartWith;
        foreach (var program in input.StartWith ?? [])
        {
            StartWithList.Items.Add(program);
        }

        PathBox.LostFocus += (_, _) => UpdateDetected();
        UpdateDetected();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = $"{Strings.T("manager.editor.browseFilter")} (*.exe)|*.exe", FileName = PathBox.Text };
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
            _iconPath = dialog.FileName;
            _iconIndex = 0;
            IconPreview.Source = _loadIcon(_iconPath, _iconIndex);
            UpdateDetected();
        }
    }

    /// <summary>Shows which startup programs of the program's folder would start with it.</summary>
    private void UpdateDetected()
    {
        var detected = string.IsNullOrWhiteSpace(PathBox.Text) ? [] : _detectStartWith(Environment.ExpandEnvironmentVariables(PathBox.Text.Trim().Trim('"')));
        DetectedText.Text = detected.Count == 0
            ? Strings.T("manager.editor.detectedNone")
            : Strings.T("manager.editor.detected", string.Join(", ", detected.Select(p => $"{p.Name} ({System.IO.Path.GetFileName(p.ExecutablePath)})")));
    }

    private void OnAddStartWith(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"{Strings.T("manager.editor.browseFilter")} (*.exe)|*.exe",
            InitialDirectory = System.IO.Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(PathBox.Text.Trim().Trim('"'))),
        };
        if (dialog.ShowDialog(this) == true && !StartWithList.Items.Contains(dialog.FileName))
        {
            StartWithList.Items.Add(dialog.FileName);
        }
    }

    private void OnRemoveStartWith(object sender, RoutedEventArgs e)
    {
        if (StartWithList.SelectedItem is { } selected)
        {
            StartWithList.Items.Remove(selected);
        }
    }

    private void OnStartWithSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        RemoveStartWithButton.IsEnabled = StartWithList.SelectedItem is not null;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var policy = FixedArgs.IsChecked == true ? CommandLinePolicy.Required
            : AnyArgs.IsChecked == true ? CommandLinePolicy.Any
            : CommandLinePolicy.None;

        // Keep the original icon unless the program itself changed.
        var pathChanged = !string.Equals(PathBox.Text.Trim(), _input.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        var request = new PublishRequest(
            PathBox.Text.Trim(),
            NameBox.Text.Trim(),
            _input.Alias,
            pathChanged && _iconPath == _input.IconPath ? null : _iconPath,
            pathChanged && _iconPath == _input.IconPath ? 0 : _iconIndex,
            policy,
            policy == CommandLinePolicy.Required ? ArgsBox.Text.Trim() : null,
            ShowBox.IsChecked == true,
            StartWithList.Items.Cast<string>().ToList(),
            AutoStartBox.IsChecked == true);

        var error = _trySave(request);
        if (error is null)
        {
            DialogResult = true;
            return;
        }

        ErrorText.Text = error;
        ErrorText.Visibility = Visibility.Visible;
    }
}
