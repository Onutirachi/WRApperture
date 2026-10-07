using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WRApperture.Client.Services;

public enum SigningSetupResult
{
    Configured,
    Cancelled,
    Failed,
}

public interface ISigningSetup
{
    /// <summary>Re-launches this app elevated (UAC) to configure RDP signing for the current user.</summary>
    Task<SigningSetupResult> ConfigureAsync();
}

internal sealed class SigningSetup : ISigningSetup
{
    public const string SetupArgument = "--setup-signing";
    public const string RemoveArgument = "--remove-signing";
    private const int ErrorCancelled = 1223;

    public async Task<SigningSetupResult> ConfigureAsync()
    {
        // The SID of the non-elevated user: an admin approving the UAC prompt may be a different account.
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{SetupArgument} {sid}")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (process is null)
            {
                return SigningSetupResult.Failed;
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? SigningSetupResult.Configured : SigningSetupResult.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return SigningSetupResult.Cancelled;
        }
    }
}
