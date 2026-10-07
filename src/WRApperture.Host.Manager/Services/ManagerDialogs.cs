using System.Windows;
using Microsoft.Win32;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Manager.Views;

namespace WRApperture.Host.Manager.Services;

/// <summary>Values shown in the app editor. Alias is null for a new app (generated on publish).</summary>
public sealed record EditorInput(
    string Title,
    string? Alias,
    string DisplayName,
    string ExecutablePath,
    CommandLinePolicy Policy,
    string? Arguments,
    string IconPath,
    int IconIndex,
    bool ShowInList);

public sealed record TokenInfo(string Token, string Port, string Addresses, string Fingerprint);

public interface IManagerDialogs
{
    /// <summary>Opens the editor; <paramref name="trySave"/> returns an error to show inline, or null when saved.</summary>
    bool EditApp(EditorInput input, Func<PublishRequest, string?> trySave);

    IReadOnlyList<DiscoveredApp>? ChooseInstalled(IReadOnlyList<DiscoveredApp> apps);

    string[]? PickPrograms();

    void ShowToken(TokenInfo info);

    bool Confirm(string title, string message);

    void Error(string message);
}

internal sealed class ManagerDialogs(IIconProvider icons) : IManagerDialogs
{
    private const string Title = "WRApperture";

    private static Window? Owner => Application.Current.MainWindow;

    public bool EditApp(EditorInput input, Func<PublishRequest, string?> trySave) =>
        new AppEditorWindow(input, trySave, (path, index) => ViewModels.Icons.Load(icons, path, index, 48)) { Owner = Owner }.ShowDialog() == true;

    public IReadOnlyList<DiscoveredApp>? ChooseInstalled(IReadOnlyList<DiscoveredApp> apps)
    {
        var window = new DiscoveryWindow(apps) { Owner = Owner };
        return window.ShowDialog() == true ? window.Selected : null;
    }

    public string[]? PickPrograms()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolha programas ou atalhos para publicar",
            Filter = "Programas e atalhos (*.exe;*.lnk)|*.exe;*.lnk",
            Multiselect = true,
            DereferenceLinks = false, // keep .lnk so its name and arguments are used
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : null;
    }

    public void ShowToken(TokenInfo info) => new TokenWindow(info) { Owner = Owner }.ShowDialog();

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public void Error(string message) =>
        MessageBox.Show(Owner!, message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
}
