using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WRApperture.Host.Manager.Services;

public static class Elevation
{
    public const string ReadOnlyArgument = "--read-only";
    private const int ErrorCancelled = 1223;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Starts an elevated copy of this app. False when the user declined the UAC prompt.</summary>
    public static bool TryRelaunchElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" })?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }
}
