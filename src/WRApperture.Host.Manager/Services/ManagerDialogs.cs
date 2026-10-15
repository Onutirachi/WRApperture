using System.Windows;
using Microsoft.Win32;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.StartWith;
using WRApperture.Host.Manager.Views;
using WRApperture.Shared.Localization;

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
    bool ShowInList,
    IReadOnlyList<string>? StartWith = null,
    bool AutoStartWith = true);

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

    /// <summary>New password for <paramref name="userName"/> (typed twice); null when cancelled.</summary>
    string? AskNewPassword(string userName);

    /// <summary>Opens a Windows settings page ("ms-settings:…").</summary>
    void OpenSettings(string uri);
}

internal sealed class ManagerDialogs(IIconProvider icons, IStartWithResolver startWith, ILocalAccounts accounts) : IManagerDialogs
{
    private const string Title = "WRApperture";

    private static Window? Owner => Application.Current.MainWindow;

    public bool EditApp(EditorInput input, Func<PublishRequest, string?> trySave) =>
        new AppEditorWindow(input, trySave, (path, index) => ViewModels.Icons.Load(icons, path, index, 48),
            path => startWith.Detect(path, ViewModels.ManagerViewModel.UserSid(accounts))) { Owner = Owner }.ShowDialog() == true;

    public IReadOnlyList<DiscoveredApp>? ChooseInstalled(IReadOnlyList<DiscoveredApp> apps)
    {
        var window = new DiscoveryWindow(apps) { Owner = Owner };
        return window.ShowDialog() == true ? window.Selected : null;
    }

    public string[]? PickPrograms()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.T("manager.pick.title"),
            Filter = $"{Strings.T("manager.pick.filter")} (*.exe;*.lnk)|*.exe;*.lnk",
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

    public string? AskNewPassword(string userName)
    {
        var window = new PasswordWindow(userName) { Owner = Owner };
        return window.ShowDialog() == true ? window.Password : null;
    }

    public void OpenSettings(string uri) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
}
