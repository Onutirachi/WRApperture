using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using WRApperture.Host.Agent.Endpoints;
using WRApperture.Host.Agent.Readiness;
using WRApperture.Host.Agent.Remote;
using WRApperture.Host.Core;
using WRApperture.Host.Core.HostInfo;

namespace WRApperture.Host.Agent.Changes;

internal sealed class HostChangesOptions
{
    /// <summary>How stale the version may be: waiting clients see a change at most this late.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(Shared.ApiContract.ChangesWaitSeconds);
}

/// <summary>
/// A version of everything clients show about this host (/api/apps and /api/host), for the long poll in
/// /api/changes. Computed at most once per <see cref="HostChangesOptions.PollInterval"/>, however many clients wait.
/// </summary>
internal sealed class HostChanges(IAppPublisher publisher, IHostInfoProvider hostInfo, HostReadiness readiness, HostVpns vpns,
    RunningApps running, HostChangesOptions options, ILogger<HostChanges> logger)
{
    private readonly object _gate = new();
    private string? _version;
    private long _computedAt;

    public string Current()
    {
        lock (_gate)
        {
            if (_version is null || Stopwatch.GetElapsedTime(_computedAt) >= options.PollInterval)
            {
                _version = Compute();
                _computedAt = Stopwatch.GetTimestamp();
            }

            return _version;
        }
    }

    /// <summary>The current version as soon as it differs from <paramref name="since"/>, or after the maximum wait.</summary>
    public async Task<string> WaitAsync(string? since, CancellationToken ct)
    {
        var deadline = Stopwatch.GetTimestamp();
        var version = Current();
        while (version == since && Stopwatch.GetElapsedTime(deadline) < options.MaxWait)
        {
            await Task.Delay(options.PollInterval, ct);
            version = Current();
        }

        return version;
    }

    private string Compute()
    {
        var text = new StringBuilder();
        try
        {
            var info = hostInfo.Get();
            text.Append(AgentInfo.Version).Append('|').Append(info.HostName).Append('|').Append(info.RdpPort).Append('\n');
            var apps = publisher.List().Where(a => a.ShowInList).ToList();
            var open = running.Find(apps);
            foreach (var app in apps)
            {
                text.Append(open.ContainsKey(app.Alias) ? '*' : '-');
                text.Append(app.Alias).Append('|').Append(app.DisplayName).Append('|').Append(app.ExecutablePath).Append('|')
                    .Append(app.IconPath).Append('|').Append(app.IconIndex).Append('\n');
            }

            var summary = readiness.Get();
            text.Append(string.Join(',', summary.Issues)).Append('|').Append(summary.SuggestedUser).Append('|').Append(summary.MicrosoftAccount).Append('\n');
            text.Append(string.Join(',', vpns.Get()));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Never break the long poll: the next computation tries again.
            logger.LogWarning(ex, "Could not compute the host's version");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())), 0, 8);
    }
}
