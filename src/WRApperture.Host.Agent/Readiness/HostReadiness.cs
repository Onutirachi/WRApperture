using WRApperture.Host.Core;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Models;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Readiness;

/// <param name="Issues"><see cref="HostIssues"/> codes.</param>
internal sealed record ReadinessSummary(IReadOnlyList<string> Issues, string? SuggestedUser, bool MicrosoftAccount);

/// <summary>What clients are told about this PC's readiness, refreshed at most once a minute.</summary>
internal sealed class HostReadiness(IHostPreparation preparation, ILocalAccounts accounts, TimeProvider time, ILogger<HostReadiness> logger)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private (ReadinessSummary Summary, DateTimeOffset At)? _cached;

    public ReadinessSummary Get()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (_cached is { } cached && now - cached.At < CacheFor)
            {
                return cached.Summary;
            }

            var summary = Compute();
            _cached = (summary, now);
            return summary;
        }
    }

    private ReadinessSummary Compute()
    {
        try
        {
            var issues = preparation.Check()
                .Where(c => c.State == ReadinessState.Pending)
                .Select(c => IssueOf(c.Id))
                .OfType<string>()
                .Distinct()
                .ToList();
            var account = accounts.ConsoleUser();
            return new ReadinessSummary(issues, account?.RemoteUserName, account?.MicrosoftAccount is not null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Never let the diagnosis break /api/host.
            logger.LogWarning(ex, "Readiness check failed");
            return new ReadinessSummary([], null, false);
        }
    }

    private static string? IssueOf(ReadinessCheckId id) => id switch
    {
        ReadinessCheckId.RdpEnabled or ReadinessCheckId.RdpPolicy or ReadinessCheckId.RdpService => HostIssues.RdpDisabled,
        ReadinessCheckId.FirewallRdp => HostIssues.FirewallBlocked,
        ReadinessCheckId.NetworkProfile => HostIssues.PublicNetwork,
        ReadinessCheckId.AccountPassword => HostIssues.AccountWithoutPassword,
        ReadinessCheckId.RemoteDesktopUsers => HostIssues.AccountNotAllowed,
        ReadinessCheckId.MicrosoftAccount => HostIssues.WindowsHelloOnly,
        _ => null,
    };
}
