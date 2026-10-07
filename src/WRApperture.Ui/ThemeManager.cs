using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace WRApperture.Ui;

public enum ThemePreference
{
    /// <summary>Follow the Windows app mode (Settings → Personalization → Colors).</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// Applies the light or dark palette to the whole app and keeps it in sync with Windows. The choice is
/// per Windows user (HKCU), shared by the client and the host manager.
/// </summary>
public static class ThemeManager
{
    private const string SettingsKey = @"Software\WRApperture\Ui";
    private const string SettingsValue = "Theme";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmUseImmersiveDarkMode = 20;

    private static readonly ResourceDictionary DarkColors = new()
    {
        Source = new Uri("pack://application:,,,/WRApperture.Ui;component/Colors.Dark.xaml"),
    };

    private static Application? _app;

    public static ThemePreference Preference { get; private set; }
    public static bool IsDark { get; private set; }

    /// <summary>Call once at startup, after the app resources (Theme.xaml) are loaded.</summary>
    public static void Initialize(Application app)
    {
        _app = app;
        Preference = LoadPreference();
        Apply();

        // Title bars follow too: every window gets the attribute when it loads.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ApplyTitleBar((Window)sender)));

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Preference == ThemePreference.System)
            {
                app.Dispatcher.BeginInvoke(Apply);
            }
        };
    }

    public static void SetPreference(ThemePreference preference)
    {
        Preference = preference;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
            key.SetValue(SettingsValue, preference.ToString());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            // Not persisted; still applied for this run.
        }

        Apply();
    }

    public static bool SystemUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    private static ThemePreference LoadPreference()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return Enum.TryParse<ThemePreference>(key?.GetValue(SettingsValue) as string, ignoreCase: true, out var value)
            ? value
            : ThemePreference.System;
    }

    private static void Apply()
    {
        if (_app is null)
        {
            return;
        }

        IsDark = Preference == ThemePreference.Dark || (Preference == ThemePreference.System && SystemUsesDarkMode());

        // Theme.xaml carries the light palette; the dark one, merged last, overrides it.
        var merged = _app.Resources.MergedDictionaries;
        merged.Remove(DarkColors);
        if (IsDark)
        {
            merged.Add(DarkColors);
        }

        foreach (Window window in _app.Windows)
        {
            ApplyTitleBar(window);
        }
    }

    private static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
