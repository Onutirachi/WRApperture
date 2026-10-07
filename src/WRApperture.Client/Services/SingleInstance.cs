using System.Threading;

namespace WRApperture.Client.Services;

/// <summary>
/// One client per Windows session: a second launch (Start menu, desktop shortcut) only asks the
/// running one to show its window, then exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\WRApperture.Client";
    private const string ShowEventName = @"Local\WRApperture.Client.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly RegisteredWaitHandle _wait;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Raised on a thread-pool thread when another launch asked to show the window.</summary>
    public event Action? ShowRequested;

    /// <summary>Null when another client already runs in this session.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        if (created)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public static void SignalRunningInstance()
    {
        if (EventWaitHandle.TryOpenExisting(ShowEventName, out var show))
        {
            using (show)
            {
                show.Set();
            }
        }
    }

    public void Dispose()
    {
        _wait.Unregister(null);
        _show.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
