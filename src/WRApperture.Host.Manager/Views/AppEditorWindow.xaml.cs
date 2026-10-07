using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Manager.Services;

namespace WRApperture.Host.Manager.Views;

/// <summary>Review/edit an app before publishing. Saving goes through the caller's callback so Core validation errors show inline.</summary>
public partial class AppEditorWindow : Window
{
    private readonly EditorInput _input;
    private readonly Func<PublishRequest, string?> _trySave;
    private readonly Func<string, int, ImageSource?> _loadIcon;
    private string _iconPath;
    private int _iconIndex;

    public AppEditorWindow(EditorInput input, Func<PublishRequest, string?> trySave, Func<string, int, ImageSource?> loadIcon)
    {
        InitializeComponent();
        _input = input;
        _trySave = trySave;
        _loadIcon = loadIcon;
        _iconPath = input.IconPath;
        _iconIndex = input.IconIndex;

        Title = input.Title;
        Heading.Text = input.Title;
        AliasText.Text = input.Alias is null ? "O identificador é gerado ao publicar." : $"Identificador: {input.Alias}";
        SaveButton.Content = input.Alias is null ? "Publicar" : "Salvar";
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
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Programas (*.exe)|*.exe", FileName = PathBox.Text };
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
            _iconPath = dialog.FileName;
            _iconIndex = 0;
            IconPreview.Source = _loadIcon(_iconPath, _iconIndex);
        }
    }

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
            ShowBox.IsChecked == true);

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
