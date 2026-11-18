using System.Diagnostics;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// Bare copies (built, no snapshot) the host keeps ready while the panel is open, so a run only has to apply its
/// snapshot. Built one at a time on a thread of its own, below normal priority, never while a run is active and never
/// below the memory reserve. Keeping one copy alive also keeps the game's resource cache filled, which makes every
/// other build in the process about four times faster.
/// </summary>
internal sealed class WarmStock(Func<bool> runActive)
{
    private readonly Lock _lock = new();
    private readonly Queue<Copy> _copies = new();
    private bool _wanted;
    private int _target;
    private bool _filling;
    private bool _duringRun;
    private long _reserveBytes = HostClient.DefaultReserveBytes;

    public int Count
    {
        get
        {
            lock (_lock)
                return _copies.Count;
        }
    }

    public int Target
    {
        get
        {
            lock (_lock)
                return _wanted ? _target : 0;
        }
    }

    public WarmState State
    {
        get
        {
            lock (_lock)
                return !_wanted ? WarmState.None
                    : _filling || _copies.Count < _target ? WarmState.Filling
                    : WarmState.Ready;
        }
    }

    /// <summary>
    /// Wants <paramref name="target"/> bare copies and starts filling, unless a run is active (it fills after). With
    /// <paramref name="duringRun"/>, it fills during the active run too, until that run ends: the next run is already
    /// known and waits for them.
    /// </summary>
    public void Warm(int target, long reserveBytes, bool duringRun = false)
    {
        lock (_lock)
        {
            _wanted = true;
            _target = Math.Max(0, target);
            _reserveBytes = reserveBytes;
            _duringRun = duringRun;
        }
        Fill();
    }

    /// <summary>Drops the stock and stops wanting it; returns how many copies it disposed.</summary>
    public int Release()
    {
        List<Copy> dropped;
        lock (_lock)
        {
            _wanted = false;
            dropped = [.. _copies];
            _copies.Clear();
        }
        foreach (var copy in dropped)
            copy.Dispose();
        return dropped.Count;
    }

    /// <summary>A bare copy for a run, or null when there is none.</summary>
    public Copy? Take()
    {
        lock (_lock)
            return _copies.TryDequeue(out var copy) ? copy : null;
    }

    /// <summary>After a run: fills again if the stock is still wanted; later runs aren't filled during.</summary>
    public void RunEnded()
    {
        lock (_lock)
            _duringRun = false;
        Fill();
    }

    private void Fill()
    {
        lock (_lock)
        {
            if (_filling || !_wanted)
                return;
            _filling = true;
        }
        new Thread(FillLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "cs2-warm",
        }.Start();
    }

    private void FillLoop()
    {
        try
        {
            SharedCaches.Fill();
            while (true)
            {
                long reserveBytes;
                int count;
                lock (_lock)
                {
                    if (!_wanted || _copies.Count >= _target || (runActive() && !_duringRun))
                        return;
                    reserveBytes = _reserveBytes;
                    count = _copies.Count;
                }
                if (MemoryReserve.For(reserveBytes)?.StockFits(count) == false)
                {
                    Log.Info($"warm: stopped at {count} copies, the memory reserve allows no more");
                    return;
                }
                var watch = Stopwatch.StartNew();
                var copy = Copy.Build();
                lock (_lock)
                {
                    // Released, or a run started meanwhile: keep it only while wanted (a run takes it from here).
                    if (_wanted)
                    {
                        _copies.Enqueue(copy);
                        copy = null;
                    }
                }
                copy?.Dispose();
                Log.Info($"warm: bare copy built in {watch.Elapsed.TotalMilliseconds:0} ms, {Count} in stock");
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error($"warm: {e}");
        }
        finally
        {
            lock (_lock)
                _filling = false;
        }
    }
}
