using System.Diagnostics;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// Runs a pool job on <c>copies</c> fight threads, each getting a prepared copy (from the warm stock, a copy built
/// ahead, or its own build) and then taking targets one after another. The first copy prepared checks every target's
/// eligibility before anything fights: refusals are reported at once, and the others wait for its answer. With reuse,
/// a copy that fought is reset and fights the next target if its fingerprint matches its prepared one; otherwise (or
/// without reuse) it is disposed and the next target gets a fresh copy. Without reuse, at most <c>min(2, copies)</c>
/// build threads keep up to 2 copies ready. All threads run below normal priority. Each target is reseeded from the
/// run seed and its key, so results don't depend on order, copies or reuse.
/// With a memory reserve (the host only), each build first asks it: building ahead stops first, then fight slots
/// wait for memory, never below one copy; if even one copy can't fit, the run fails.
/// No target kind is kept from reuse: every reset is checked against the prepared fingerprint, and enemies and
/// dungeons alike reset clean.
/// A master's monster given a loadout of its own wears it for its fight, swapped in its copy, and the reset after puts
/// the setup back; such targets queue together by loadout.
/// </summary>
internal sealed partial class CopyPool
{
    private const int PreBuilt = 2;

    /// <summary>Progress reports per fighting target are at least this far apart.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly PoolJob _job;
    private readonly IPoolReport _report;
    private readonly CancellationTokenSource _stop;
    private readonly CancellationToken _token;
    private readonly MemoryReserve? _reserve;
    private readonly Func<Copy?>? _stock;
    private readonly int _slots; // fight slots: the copies asked, fewer with fewer targets or short memory
    private readonly int _short;
    private Exception? _failure;
    private int _memoryWaiting;
    private int _memoryWaitingPeak;
    private readonly object _gate = new();
    private readonly Queue<TargetSpec> _pending = new();
    private readonly Dictionary<string, Eligibility> _eligibility = new(StringComparer.Ordinal);
    private readonly Queue<Copy> _ready = new();
    private readonly TaskCompletionSource _checked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _checkClaimed;
    private int _building;
    private int _live;
    private int _peak;
    private int _built;
    private int _preBuiltUsed;
    private int _fromStock;
    private int _resets;
    private int _misses;
    private readonly SlotClock _clock;
    private int _mainSkillReported;
    private CopyFailures? _copyFailures;
    private readonly HashSet<string> _missLogged = new(StringComparer.Ordinal); // each differing part logged once

    private CopyPool(
        PoolJob job,
        IPoolReport report,
        MemoryReserve? reserve,
        Func<Copy?>? stock,
        int stockCopies,
        CancellationToken token
    )
    {
        var slots = Math.Min(job.Copies, job.Targets.Count);
        _slots = reserve?.Fighters(slots, stockCopies) ?? slots;
        _short = slots - _slots;
        _clock = new SlotClock(_slots);
        _job = job;
        _report = report;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        _token = _stop.Token;
        _reserve = reserve;
        _stock = stock;
    }

    /// <summary>Copies alive or being built now, built-ahead ones included.</summary>
    public int Live => Volatile.Read(ref _live);

    private int _fighting;

    /// <summary>
    /// Fight slots working on a target now (building its copy or fighting): at most the copies asked.
    /// </summary>
    public int Fighting => Volatile.Read(ref _fighting);

    /// <summary>The fewest fight slots that were not waiting for memory at any moment of the run.</summary>
    public int MinCopies => Math.Max(1, _job.Copies - _short - Volatile.Read(ref _memoryWaitingPeak));

    /// <summary>
    /// Starts the run on a coordinator thread and returns at once. <paramref name="stock"/> hands out bare copies
    /// built before the run (the host's warm stock), or null when it has none left; <paramref name="stockCopies"/>
    /// is how many it holds now.
    /// </summary>
    public static CopyPool Start(
        PoolJob job,
        IPoolReport report,
        CancellationToken token,
        MemoryReserve? reserve = null,
        Func<Copy?>? stock = null,
        int stockCopies = 0
    )
    {
        var pool = new CopyPool(job, report, reserve, stock, stockCopies, token);
        PoolThreads.Start("cs2-run", pool.Coordinate);
        return pool;
    }

    private void Coordinate()
    {
        var wall = Stopwatch.StartNew();
        try
        {
            SharedCaches.Fill();
            if (_short > 0)
                Log.Info($"{_slots} of {_job.Copies} copies fight: the memory reserve allows no more");
            _copyFailures = new CopyFailures(_slots, _report, TakeNext, WorkLeft, Fail);
            var threads = new List<Thread>();
            for (var i = 0; i < _slots; i++)
            {
                var slot = i + 1;
                threads.Add(PoolThreads.Start($"cs2-fight-{slot}", () => Fight(slot)));
            }
            if (!_job.Reuse && _job.Targets.Count > _slots)
            {
                for (var i = 0; i < Math.Min(PreBuilt, _job.Copies); i++)
                    threads.Add(PoolThreads.Start($"cs2-build-{i + 1}", BuildAhead));
            }
            foreach (var thread in threads)
                thread.Join();
            if (_failure != null)
                throw _failure;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _report.Failed(e);
            return;
        }
        finally
        {
            lock (_gate)
            {
                while (_ready.TryDequeue(out var copy))
                    Dispose(copy);
            }
            _stop.Dispose();
        }
        var counts = new PoolStats(_peak, _built, _preBuiltUsed, 0, 0, FromStock: _fromStock, Resets: _resets);
        _report.Finished(_clock.End(counts with { ResetMisses = _misses, WallSeconds = wall.Elapsed.TotalSeconds }));
    }

    /// <summary>
    /// On the first copy prepared, before it fights: judges every target as of the run's snapshot, reports the
    /// refused ones, and queues the rest. Checking doesn't change the copy, so it then fights like the others.
    /// </summary>
    private void CheckAll(Copy copy)
    {
        using var scope = LogScope.Push(phase: RunPhase.Plan);
        var watch = Stopwatch.StartNew();
        try
        {
            foreach (var target in _job.Targets.OrderBy(t => _job.LoadoutOf(t.Key), StringComparer.Ordinal))
            {
                if (_token.IsCancellationRequested)
                    return;
                var eligibility = Targets.Check(copy.Game, target);
                if (eligibility.Eligible || _job.Options.IgnoreRequirements)
                {
                    lock (_gate)
                    {
                        _eligibility[target.Key] = eligibility;
                        _pending.Enqueue(target);
                    }
                    continue;
                }
                var result = new TargetResult
                {
                    Key = target.Key,
                    Kind = target.Kind,
                    State = TargetState.Refused,
                };
                result.Reasons.AddRange(eligibility.Reasons);
                _report.Done(result);
            }
        }
        finally
        {
            _clock.Add(SlotWork.Check, watch.Elapsed.Ticks);
            StartStages.Mark("checked");
            lock (_gate)
                Monitor.PulseAll(_gate);
            _checked.TrySetResult();
        }
    }

    // True once the targets are checked; the first caller with a prepared copy checks them.
    private bool WaitForCheck(Copy copy)
    {
        if (Interlocked.Exchange(ref _checkClaimed, 1) == 0)
            CheckAll(copy);
        try
        {
            _checked.Task.Wait(_token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        return true;
    }

    private void Fight(int slot)
    {
        using var scope = LogScope.Push(copy: slot);
        SlotClock.Enter(slot);
        Copy? copy = null;
        try
        {
            while (!_token.IsCancellationRequested)
            {
                if (copy == null)
                {
                    try
                    {
                        using (LogScope.Push(phase: RunPhase.Build))
                            copy = Acquire();
                    }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    {
                        _copyFailures!.Failed(e);
                        return;
                    }
                    if (copy == null || !WaitForCheck(copy))
                        return;
                }
                TargetSpec target;
                Eligibility eligibility;
                lock (_gate)
                {
                    if (!_pending.TryDequeue(out target!))
                        return;
                    eligibility = _eligibility[target.Key];
                }
                var wore = _job.LoadoutOf(target.Key) is { } loadout ? Wear(copy, loadout, target) : null;
                FightOne(slot, copy, target, eligibility);
                if (wore != null && _job.Reuse && !_token.IsCancellationRequested)
                    Wear(copy, wore, target); // the game's swap back empties what reading the save back leaves
                if (_job.Reuse && !_token.IsCancellationRequested && Reset(copy, target))
                    continue;
                Dispose(copy);
                copy = null;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Fail(e);
        }
        finally
        {
            _clock.Leave();
            if (copy != null)
                Dispose(copy);
        }
    }

    private void FightOne(int slot, Copy copy, TargetSpec target, Eligibility eligibility)
    {
        using var scope = LogScope.Push(phase: RunPhase.Fight, target: target.Key);
        var watch = Stopwatch.StartNew();
        Interlocked.Increment(ref _fighting);
        _clock.CountFight();
        try
        {
            _report.State(target.Key, TargetState.Fighting);
            BeforeFight(target.Key);
            var seed = _job.SeedFor(target.Key);
            copy.Reseed(seed, target.SeedKey);
            var lastProgress = 0L;
            var result = Driver.Fight(
                copy,
                target,
                eligibility,
                _job.Options,
                _token,
                trials =>
                {
                    var now = watch.ElapsedTicks;
                    if (now - lastProgress < ProgressInterval.TotalSeconds * Stopwatch.Frequency)
                        return;
                    lastProgress = now;
                    _report.Progress(target.Key, trials);
                }
            );
            result.RealSeconds = watch.Elapsed.TotalSeconds;
            if (seed != _job.Seed)
                result.RunSeed = seed;
            Fought(slot, copy, target, seed, result);
            _report.Done(result);
            StartStages.FirstResult();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("fight failed", e);
            _report.Done(CopyFailures.Result(target, e));
        }
        finally
        {
            Interlocked.Decrement(ref _fighting);
            _clock.Add(SlotWork.Fight, watch.Elapsed.Ticks);
        }
    }

    // True when the copy is back to its prepared state and may fight again.
    private bool Reset(Copy copy, TargetSpec target)
    {
        using var scope = LogScope.Push(phase: RunPhase.Build, target: target.Key);
        var watch = Stopwatch.StartNew();
        IReadOnlyList<string> left;
        try
        {
            left = copy.Reset();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.WarnOnce("reset failed, a fresh copy fights next", e);
            left = [$"{e.GetType().Name}: {e.Message}"];
        }
        _clock.Add(SlotWork.Reset, watch.Elapsed.Ticks);
        Interlocked.Increment(ref _resets);
        if (left.Count == 0)
            return true;
        Interlocked.Increment(ref _misses);
        bool first;
        lock (_missLogged)
            first = left.Any(_missLogged.Add);
        if (first)
            Log.Warn($"reset after {target.Key} left {string.Join(", ", left)}: a fresh copy fights next");
        return false;
    }

    private TargetSpec? TakeNext()
    {
        lock (_gate)
            return _pending.TryDequeue(out var target) ? target : null;
    }

    // Targets wait for a slot, or none has been checked yet because no copy got that far.
    private bool WorkLeft()
    {
        lock (_gate)
            return _pending.Count > 0 || Volatile.Read(ref _checkClaimed) == 0;
    }

    private void Fail(Exception error)
    {
        Interlocked.CompareExchange(ref _failure, error, null);
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run has ended and disposed its stop: there is nothing left to stop.
        }
    }

    static partial void BeforeFight(string key);

    static partial void BeforePrepare();

    // Where a fight slot's copy came from, and each target it fought: the dev record of what a copy went through.
    static partial void Acquired(Copy copy, string origin);

    partial void Fought(int slot, Copy copy, TargetSpec target, ulong seed, TargetResult result);

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value)
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
                return;
        }
    }
}
