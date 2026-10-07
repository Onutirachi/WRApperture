using WRApperture.Shared.Updates;

namespace WRApperture.Host.Agent.Updates;

/// <summary>Runs the host setup (seam for tests: they never start a process).</summary>
internal interface IHostSetupRunner
{
    /// <summary>Starts the verified setup silently and returns its exit code, if this process lives to see it.</summary>
    Task<int> RunAsync(VerifiedSetup setup, CancellationToken ct);
}

internal sealed class HostSetupRunner : IHostSetupRunner
{
    public async Task<int> RunAsync(VerifiedSetup setup, CancellationToken ct)
    {
        using var process = SetupLauncher.Start(setup, UpdateSource.SilentArguments);
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
