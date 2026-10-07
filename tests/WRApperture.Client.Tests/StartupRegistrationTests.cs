using Microsoft.Win32;
using WRApperture.Client.Core.Startup;

namespace WRApperture.Client.Tests;

/// <summary>Uses a throwaway HKCU key, never the real Run key.</summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _key = $@"Software\WRApperture.Tests.{Guid.NewGuid():N}";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);

    [Fact]
    public void Enable_writes_a_tray_command_and_disable_removes_it()
    {
        var startup = new StartupRegistration(_key);
        Assert.False(startup.IsEnabled);

        startup.Set(true, @"C:\Apps\WRApperture\WRApperture.exe");

        Assert.True(startup.IsEnabled);
        using (var key = Registry.CurrentUser.OpenSubKey(_key))
        {
            Assert.Equal("\"C:\\Apps\\WRApperture\\WRApperture.exe\" --tray", key!.GetValue(StartupRegistration.ValueName));
        }

        startup.Set(false, @"C:\Apps\WRApperture\WRApperture.exe");
        Assert.False(startup.IsEnabled);
        startup.Set(false, "x"); // idempotent
    }

    [Fact]
    public void Refresh_updates_the_path_only_when_enabled()
    {
        var startup = new StartupRegistration(_key);

        startup.Refresh(@"C:\New\WRApperture.exe");
        Assert.False(startup.IsEnabled);

        startup.Set(true, @"C:\Old\WRApperture.exe");
        startup.Refresh(@"C:\New\WRApperture.exe");
        using var key = Registry.CurrentUser.OpenSubKey(_key);
        Assert.Equal(StartupRegistration.CommandFor(@"C:\New\WRApperture.exe"), key!.GetValue(StartupRegistration.ValueName));
    }
}
