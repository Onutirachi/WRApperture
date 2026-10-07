using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Platform;
using WRApperture.Shared.Localization;
using WRApperture.Shared.Updates;

namespace WRApperture.Host.Manager.ViewModels;

/// <summary>"Updates" section: versions, the agent's AutoUpdate setting and "Update now" (the setup's wizard, not silent).</summary>
public sealed partial class ManagerViewModel
{
    private ReleaseInfo? _latest;

    public string CurrentVersion { get; } = ProductVersion.Format(ProductVersion.Current);

    [ObservableProperty] private string _latestVersion = Strings.T("manager.updates.checking");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    private bool _isUpdating;

    /// <summary>HKLM\SOFTWARE\WRApperture\Agent\AutoUpdate; the agent reads it before each scheduled install.</summary>
    public bool AutoUpdate
    {
        get => _agentSettings.Load().AutoUpdate;
        set
        {
            if (value == AutoUpdate)
            {
                return;
            }

            try
            {
                _agentSettings.SetAutoUpdate(value);
                SetStatus(value ? Strings.T("manager.status.autoUpdateOn") : Strings.T("manager.status.autoUpdateOff"), warning: false);
            }
            catch (Exception ex) when (ex is AdminRequiredException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogError(ex, "Could not change automatic updates");
                _dialogs.Error(ex.Message);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Asks GitHub once when the window opens (the agent itself checks every 6 hours).</summary>
    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        LatestVersion = Strings.T("manager.updates.checking");
        try
        {
            _latest = await _updates.GetLatestAsync();
            IsUpdateAvailable = _latest?.IsNewerThan(ProductVersion.Current) == true;
            LatestVersion = _latest is null ? Strings.T("manager.updates.none")
                : IsUpdateAvailable ? Strings.T("manager.updates.available", ProductVersion.Format(_latest.Version))
                : Strings.T("manager.updates.upToDate");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Update check failed: {Error}", ex.Message);
            LatestVersion = Strings.T("manager.updates.checkFailed");
        }
    }

    /// <summary>Downloads and verifies the host setup, opens its wizard (elevated, like this window) and closes.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateNow))]
    private async Task UpdateNowAsync()
    {
        if (_latest is not { } release ||
            !_dialogs.Confirm(Strings.T("manager.updates.confirmTitle"), Strings.T("manager.updates.confirm", ProductVersion.Format(release.Version))))
        {
            return;
        }

        IsUpdating = true;
        SetStatus(Strings.T("manager.status.downloadingUpdate", ProductVersion.Format(release.Version)), warning: false);
        try
        {
            var folder = await Task.Run(() => UpdateFolder.Prepare("manager"));
            var setup = await _updates.DownloadVerifiedAsync(release, UpdateProduct.Host, folder);
            _logger.LogInformation("Starting host setup {Setup} (SHA-256 {Hash}) and closing", setup.Path, setup.Sha256);
            SetupLauncher.Start(setup, string.Empty).Dispose();
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UpdateVerificationException or IOException
                                       or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Updating to {Latest} failed", release.Tag);
            SetStatus(Strings.T("manager.errors.updateFailed", ex.Message), warning: true);
            _dialogs.Error(Strings.T("manager.errors.updateFailed", ex.Message));
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private bool CanUpdateNow() => IsAdmin && IsUpdateAvailable && !IsUpdating;
}
