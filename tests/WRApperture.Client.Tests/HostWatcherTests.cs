using WRApperture.Client.Core;
using WRApperture.Client.Core.Agent;
using WRApperture.Shared;

namespace WRApperture.Client.Tests;

public class HostWatcherTests
{
    private readonly ScriptedAgent _agent = new();
    private readonly List<HostEvent> _events = [];
    private readonly HostWatcher _watcher;

    public HostWatcherTests() => _watcher = new HostWatcher(_ => _agent);

    /// <summary>Runs the watch until the script is used up (then the agent "waits" and the watch is cancelled).</summary>
    private async Task RunAsync()
    {
        using var cts = new CancellationTokenSource();
        _agent.Exhausted = cts.Cancel;
        var watch = _watcher.WatchAsync(() => Samples.Host(), e =>
        {
            _events.Add(e);
            return Task.CompletedTask;
        }, cts.Token, retryDelay: TimeSpan.Zero);
        var finished = await Task.WhenAny(watch, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(watch, finished);
        await watch;
    }

    [Fact]
    public async Task First_version_is_only_remembered_then_each_new_one_is_a_change()
    {
        _agent.Script.Enqueue(_ => "v1");
        _agent.Script.Enqueue(_ => "v1");
        _agent.Script.Enqueue(_ => "v2");
        _agent.Script.Enqueue(_ => "v3");

        await RunAsync();

        Assert.Equal([HostEvent.Changed, HostEvent.Changed], _events);
        Assert.Equal([null, "v1", "v1", "v2", "v3"], _agent.Asked);
    }

    [Fact]
    public async Task Unreachable_is_reported_once_and_coming_back_is_a_change()
    {
        _agent.Script.Enqueue(_ => "v1");
        _agent.Script.Enqueue(_ => throw new AgentUnreachableException("pc", 5080));
        _agent.Script.Enqueue(_ => throw new AgentUnreachableException("pc", 5080));
        _agent.Script.Enqueue(_ => "v1");

        await RunAsync();

        Assert.Equal([HostEvent.Unreachable, HostEvent.Changed], _events);
    }

    [Fact]
    public async Task Refused_token_is_a_change_and_ends_the_watch()
    {
        _agent.Script.Enqueue(_ => "v1");
        _agent.Script.Enqueue(_ => throw new InvalidTokenException { RefusedByHost = true });
        _agent.Script.Enqueue(_ => "never asked");

        await RunAsync();

        Assert.Equal([HostEvent.Changed], _events);
        Assert.Single(_agent.Script);
    }

    [Fact]
    public async Task Error_answers_are_retried_without_events()
    {
        _agent.Script.Enqueue(_ => "v1");
        _agent.Script.Enqueue(_ => throw new AgentException("500"));
        _agent.Script.Enqueue(_ => "v1");

        await RunAsync();

        Assert.Empty(_events);
    }

    private sealed class ScriptedAgent : IAgentApi
    {
        public Queue<Func<string?, string>> Script { get; } = new();
        public List<string?> Asked { get; } = [];
        public Action? Exhausted { get; set; }

        public async Task<string> WaitForChangesAsync(string? since, CancellationToken ct = default)
        {
            Asked.Add(since);
            if (Script.TryDequeue(out var next))
            {
                return next(since);
            }

            Exhausted?.Invoke();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        }

        public Task<HostInfoDto> GetHostInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AppSummaryDto>> GetAppsAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IconResult> GetIconAsync(string alias, int size, string? etag, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionStatusDto?> GetSessionStatusAsync(string? rdpUser, bool desktop = false, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SessionReleaseResultDto> ReleaseSessionAsync(string? rdpUser, bool desktop = false, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<UpdateRequestResultDto> RequestHostUpdateAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<UpdateStatusDto> GetHostUpdateStatusAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> StopAppAsync(string alias, bool force, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> EndSessionAsync(string? rdpUser, CancellationToken ct = default) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
