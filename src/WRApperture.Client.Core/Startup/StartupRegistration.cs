using Microsoft.Win32;

namespace WRApperture.Client.Core.Startup;

/// <summary>"Start with Windows": a per-user Run entry that starts the client hidden in the tray.</summary>
public sealed class StartupRegistration(string runKeyPath = StartupRegistration.DefaultRunKey)
{
    public const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "WRApperture";
    public const string TrayArgument = "--tray";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static string CommandFor(string exePath) => $"\"{exePath}\" {TrayArgument}";

    public void Set(bool enabled, string exePath)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(runKeyPath);
            key.SetValue(ValueName, CommandFor(exePath));
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Keeps an existing entry pointing at the running exe (e.g. after reinstalling elsewhere).</summary>
    public void Refresh(string exePath)
    {
        if (IsEnabled)
        {
            Set(true, exePath);
        }
    }
}
