using System.Diagnostics;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

internal sealed partial class CopyPool
{
    // Gets a prepared copy for a fight slot: stock, then built ahead, then its own build. Null when the run stopped.
    private Copy? Acquire()
    {
        if (_stock?.Invoke() is { } bare)
        {
            Interlocked.Increment(ref _fromStock);
            var live = Interlocked.Increment(ref _live);
            InterlockedMax(ref _peak, live);
            try
            {
                Acquired(bare, "stock");
                Prepare(bare);
                return bare;
            }
            catch
            {
                Dispose(bare);
                throw;
            }
        }
        lock (_gate)
        {
            if (_ready.TryDequeue(out var ready))
            {
                _preBuiltUsed++;
                Monitor.PulseAll(_gate);
                Acquired(ready, "ahead");
                return ready;
            }
        }
        if (!WaitForMemory(preBuild: false))
            return null;
        return _token.IsCancellationRequested ? null : NewCopy();
    }

    private void BuildAhead()
    {
        using var scope = LogScope.Push(phase: RunPhase.Build);
        // Built-ahead copies are for targets that passed the check.
        try
        {
            _checked.Task.Wait(_token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        while (!_token.IsCancellationRequested)
        {
            lock (_gate)
            {
                // Build only while fewer than 2 are ready or building, and only for targets nobody has taken yet.
                while (_ready.Count + _building >= Math.Min(PreBuilt, _pending.Count))
                {
                    if (_pending.Count == 0 || _token.IsCancellationRequested)
                        return;
                    Monitor.Wait(_gate, 100);
                }
                _building++;
            }
            if (_reserve != null && _reserve.Decide(Live, preBuild: true) != ReserveDecision.Build)
            {
                lock (_gate)
                    _building--;
                _token.WaitHandle.WaitOne(200);
                continue;
            }
            Copy? copy = null;
            try
            {
                copy = NewCopy();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // A copy built ahead fights nothing yet: the fight slots build their own instead.
                Log.Error("copy built ahead failed, building ahead stops", e);
                return;
            }
            finally
            {
                lock (_gate)
                {
                    _building--;
                    if (copy != null)
                    {
                        if (_token.IsCancellationRequested || _pending.Count == 0)
                            Dispose(copy); // finished detached: nothing will fight in it
                        else
                            _ready.Enqueue(copy);
                    }
                }
            }
        }
    }

    // A fight slot about to build: true to go ahead, false when the run stopped or memory ran out (which fails it).
    private bool WaitForMemory(bool preBuild)
    {
        if (_reserve == null)
            return true;
        var waiting = false;
        Stopwatch? shortSince = null;
        try
        {
            while (!_token.IsCancellationRequested)
            {
                var decision = _reserve.Decide(Live, preBuild);
                // Disposed copies are garbage until collected, and count as used: free them before waiting or failing.
                if (decision != ReserveDecision.Build && CollectGarbage())
                    decision = _reserve.Decide(Live, preBuild);
                switch (decision)
                {
                    case ReserveDecision.Build:
                        return true;
                    // A copy fitted before, so memory is likely garbage or another program's for a moment: wait for it,
                    // up to 30 s with nothing of ours left alive. Not even the first copy fitting fails at once.
                    case ReserveDecision.OutOfMemory
                        when Volatile.Read(ref _built) == 0
                            || (shortSince ??= Stopwatch.StartNew()).Elapsed.TotalSeconds > 30:
                        Fail(new NotEnoughMemoryException(_reserve.OutOfMemoryReason()));
                        return false;
                    case ReserveDecision.OutOfMemory:
                        break;
                    default:
                        shortSince = null;
                        break;
                }
                if (!waiting)
                {
                    waiting = true;
                    InterlockedMax(ref _memoryWaitingPeak, Interlocked.Increment(ref _memoryWaiting));
                }
                _token.WaitHandle.WaitOne(200);
            }
            return false;
        }
        finally
        {
            if (waiting)
                Interlocked.Decrement(ref _memoryWaiting);
        }
    }

    private static long _lastCollect;

    // One full, compacting collection at most every 2 s across the pool's threads; true if this call ran it.
    private static bool CollectGarbage()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastCollect);
        if (now - last < 2000 || Interlocked.CompareExchange(ref _lastCollect, now, last) != last)
            return false;
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System
            .Runtime
            .GCLargeObjectHeapCompactionMode
            .CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        return true;
    }

    private Copy NewCopy()
    {
        var watch = Stopwatch.StartNew();
        var live = Interlocked.Increment(ref _live);
        InterlockedMax(ref _peak, live);
        try
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var copy = Copy.Build();
            Interlocked.Increment(ref _built);
            _clock.Add(SlotWork.Build, watch.Elapsed.Ticks);
#if DEBUG
            var built = GC.GetAllocatedBytesForCurrentThread();
#endif
            try
            {
                Prepare(copy);
            }
            catch
            {
                copy.Dispose();
                throw;
            }
            // What this thread allocated for the copy, garbage included: an estimate on the safe side.
            _reserve?.Built(GC.GetAllocatedBytesForCurrentThread() - allocated);
#if DEBUG
            Common.Log.Info(
                $"alloc: build {(built - allocated) / 1048576.0:0} MB in {copy.BuildMs:0} ms, prepare {(GC.GetAllocatedBytesForCurrentThread() - built) / 1048576.0:0} MB in {copy.ApplyMs:0} ms, {live} live"
            );
#endif
            return copy;
        }
        catch
        {
            Interlocked.Decrement(ref _live);
            throw;
        }
    }

    // The run's setup applied to a bare copy, and kept as it is for resets when copies are reused.
    private void Prepare(Copy copy)
    {
        BeforePrepare();
        var watch = Stopwatch.StartNew();
        copy.Prepare(_job.Snapshot, _job.LoadoutSlot, _job.Changes, _job.Options.IgnoreRequirements);
        if (_job.Reuse)
            copy.KeepPristine();
        _clock.Add(SlotWork.Prepare, watch.Elapsed.Ticks);
        if (Interlocked.Exchange(ref _mainSkillReported, 1) == 0)
            _report.MainSkill(copy.MainSkill);
    }

    // A loadout put on, counted as preparing; returns the one the copy wore before.
    private string Wear(Copy copy, string loadout, TargetSpec target)
    {
        using var scope = LogScope.Push(phase: RunPhase.Build, target: target.Key);
        var watch = Stopwatch.StartNew();
        var before = copy.CurrentLoadout;
        copy.WearLoadout(loadout);
        _clock.Add(SlotWork.Prepare, watch.Elapsed.Ticks);
        return before;
    }

    private void Dispose(Copy copy)
    {
        copy.Dispose();
        Interlocked.Decrement(ref _live);
    }
}
