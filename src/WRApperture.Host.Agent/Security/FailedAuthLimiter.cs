using System.Collections.Concurrent;

namespace WRApperture.Host.Agent.Security;

/// <summary>
/// Blocks a source address for <see cref="BlockDuration"/> after <see cref="MaxFailures"/>
/// failed authentications within <see cref="Window"/>. Successful requests are never limited.
/// </summary>
internal sealed class FailedAuthLimiter(TimeProvider time)
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(5);
    private const int CleanupThreshold = 1000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public bool IsBlocked(string source)
    {
        if (!_entries.TryGetValue(source, out var entry))
        {
            return false;
        }

        lock (entry)
        {
            return entry.BlockedUntil is { } until && until > time.GetUtcNow();
        }
    }

    public void RecordFailure(string source)
    {
        var now = time.GetUtcNow();
        var entry = _entries.GetOrAdd(source, _ => new Entry());
        lock (entry)
        {
            entry.Failures.Enqueue(now);
            while (entry.Failures.TryPeek(out var oldest) && now - oldest > Window)
            {
                entry.Failures.Dequeue();
            }

            if (entry.Failures.Count >= MaxFailures)
            {
                entry.BlockedUntil = now + BlockDuration;
                entry.Failures.Clear();
            }
        }

        if (_entries.Count > CleanupThreshold)
        {
            Cleanup(now);
        }
    }

    public void RecordSuccess(string source) => _entries.TryRemove(source, out _);

    private void Cleanup(DateTimeOffset now)
    {
        foreach (var (key, entry) in _entries)
        {
            lock (entry)
            {
                var notBlocked = (entry.BlockedUntil ?? DateTimeOffset.MinValue) <= now;
                var noRecentFailures = !entry.Failures.TryPeek(out var oldest) || now - oldest > Window;
                var idle = notBlocked && noRecentFailures;
                if (idle)
                {
                    _entries.TryRemove(key, out _);
                }
            }
        }
    }

    private sealed class Entry
    {
        public Queue<DateTimeOffset> Failures { get; } = new();
        public DateTimeOffset? BlockedUntil { get; set; }
    }
}
