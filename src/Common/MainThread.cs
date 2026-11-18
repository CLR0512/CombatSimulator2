using System.Collections.Concurrent;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>
/// Work handed to the main thread, run from a Core <c>OnFrame</c> callback: worker output and bridge replies that
/// come later. Bounded per frame so a burst never stalls one.
/// </summary>
internal static class MainThread
{
    private const int MaxActionsPerFrame = 64;

    private static readonly ConcurrentQueue<Action> _queue = new();
    private static readonly List<Waiter> _waiters = [];
    private static int _mainThreadId = -1;

    private sealed record Waiter(Func<bool> Ready, Action Then, long Deadline, Action OnTimeout);

    private static bool IsCurrent => Environment.CurrentManagedThreadId == _mainThreadId;

    /// <summary>Records the main thread; called from <c>Load</c>, which runs on it.</summary>
    public static void Init() => _mainThreadId = Environment.CurrentManagedThreadId;

    public static void Post(Action action) => _queue.Enqueue(action);

    public static void Assert(string what)
    {
        if (!IsCurrent)
            throw new InvalidOperationException($"{what} must run on the main thread");
    }

    /// <summary>
    /// Runs <paramref name="then"/> on the first frame where <paramref name="ready"/> holds, or
    /// <paramref name="onTimeout"/> once <paramref name="timeout"/> has passed. For bridge replies that come later.
    /// </summary>
    public static void When(Func<bool> ready, Action then, TimeSpan timeout, Action onTimeout)
    {
        Assert("MainThread.When");
        if (ready())
        {
            then();
            return;
        }
        _waiters.Add(new Waiter(ready, then, Environment.TickCount64 + (long)timeout.TotalMilliseconds, onTimeout));
    }

    /// <summary>
    /// Runs queued work and due waiters, once a frame. A failing action is logged; the rest still run.
    /// </summary>
    public static void Drain()
    {
        for (var i = 0; i < MaxActionsPerFrame && _queue.TryDequeue(out var action); i++)
            Try(action, "main-thread work");
        if (_waiters.Count == 0)
            return;
        var now = Environment.TickCount64;
        foreach (var waiter in _waiters.ToArray())
        {
            if (waiter.Ready())
            {
                _waiters.Remove(waiter);
                Try(waiter.Then, "main-thread waiter");
            }
            else if (now >= waiter.Deadline)
            {
                _waiters.Remove(waiter);
                Try(waiter.OnTimeout, "main-thread waiter");
            }
        }
    }

    public static void Clear()
    {
        _queue.Clear();
        _waiters.Clear();
    }

    private static void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error($"{what} failed: {e}");
        }
    }
}
