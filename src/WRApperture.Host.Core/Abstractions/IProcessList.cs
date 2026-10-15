namespace WRApperture.Host.Core.Abstractions;

/// <param name="Path">Full path of the program; null when Windows does not tell (protected processes).</param>
public sealed record RunningProcess(int Id, int SessionId, string? Path);

/// <summary>Processes running on this PC.</summary>
public interface IProcessList
{
    /// <summary>The processes of the given sessions, with their program paths (requires LocalSystem for other users).</summary>
    IReadOnlyList<RunningProcess> InSessions(IReadOnlySet<int> sessionIds);
}
