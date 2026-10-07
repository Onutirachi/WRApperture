using System.Diagnostics;

namespace WRApperture.Shared.Updates;

/// <summary>Starts a verified setup, checking it once more right before it runs.</summary>
public static class SetupLauncher
{
    /// <summary>
    /// Opens the file denying writes and deletes, hashes it again and starts it while the handle is held, so it
    /// cannot be swapped between the check and the start. The child is a plain process: it outlives this one
    /// (the host setup stops the agent's service; the client exits right after starting it).
    /// </summary>
    /// <exception cref="UpdateVerificationException">The file changed since it was verified.</exception>
    public static Process Start(VerifiedSetup setup, string arguments)
    {
        using var guard = new FileStream(setup.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = UpdateChecker.Sha256Of(guard);
        if (!string.Equals(actual, setup.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateVerificationException($"{setup.Path} changed after it was verified");
        }

        return Process.Start(new ProcessStartInfo(setup.Path, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(setup.Path)!,
        }) ?? throw new InvalidOperationException($"{setup.Path} did not start");
    }
}
