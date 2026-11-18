using System.Diagnostics;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// Fights a search's candidates. Each fight slot has one copy prepared with the base and takes candidate after
/// candidate: the set's changes are applied in place by the game's own equip path, it fights from the search's
/// seed, and the copy is put back as prepared. A copy that doesn't come back as prepared is dropped and replaced.
/// The copies are kept between the stages of one search and dropped when the pool is disposed. Threads, the warm
/// stock and the memory reserve are used as a run's pool uses them.
/// </summary>
internal sealed class SearchPool(MemoryReserve? reserve = null, Func<Copy?>? stock = null) : IDisposable
{
    private readonly object _gate = new();

    // Each with the skill the base trains: a copy's own reading follows the last set it wore.
    private readonly Stack<(Copy Copy, CopyUndo Undo, string? Skill)> _kept = new();
    private bool _disposed;
    private int _live;
    private int _peak;
    private int _built;
    private int _fromStock;
    private int _dropped;
    private int _fighting;

    /// <summary>Copies alive now, kept ones included.</summary>
    public int Live => Volatile.Read(ref _live);

    /// <summary>Fight slots working on a candidate now.</summary>
    public int Fighting => Volatile.Read(ref _fighting);

    /// <summary>Starts a stage on a coordinator thread and returns at once.</summary>
    public void Start(SearchJob job, ISearchReport report, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(report);
        PoolThreads.Start("cs2-search", () => Coordinate(job, report, token));
    }

    /// <summary>Drops the copies kept between stages; a stage still running drops its own as its slots end.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            while (_kept.TryPop(out var kept))
                Drop(kept.Copy);
        }
    }

    private void Coordinate(SearchJob job, ISearchReport report, CancellationToken token)
    {
        var wall = Stopwatch.StartNew();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pending = new Queue<Candidate>(job.Candidates);
        Exception? failure = null;
        void Fail(Exception error)
        {
            Interlocked.CompareExchange(ref failure, error, null);
            stop.Cancel();
        }
        try
        {
            SharedCaches.Fill();
            var wanted = Math.Min(job.Copies, job.Candidates.Count);
            var slots = Math.Max(1, reserve?.Fighters(wanted, 0) ?? wanted);
            var threads = new List<Thread>();
            for (var i = 0; i < slots; i++)
            {
                var slot = i + 1;
                threads.Add(
                    PoolThreads.Start($"cs2-search-{slot}", () => Fight(slot, job, report, pending, Fail, stop.Token))
                );
            }
            foreach (var thread in threads)
                thread.Join();
            if (failure != null)
                throw failure;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            report.Failed(e);
            return;
        }
        report.Finished(
            new PoolStats(_peak, _built, 0, 0, wall.Elapsed.TotalSeconds, FromStock: _fromStock, ResetMisses: _dropped)
        );
    }

    private void Fight(
        int slot,
        SearchJob job,
        ISearchReport report,
        Queue<Candidate> pending,
        Action<Exception> fail,
        CancellationToken token
    )
    {
        using var scope = LogScope.Push(copy: slot, target: job.Target.Key);
        (Copy Copy, CopyUndo Undo, string? Skill)? held = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                held ??= Acquire(job, token);
                if (held is not { } mine)
                    return;
                Candidate candidate;
                lock (pending)
                {
                    if (!pending.TryDequeue(out candidate!))
                        return;
                }
                if (!FightOne(job, report, mine.Copy, mine.Undo, mine.Skill, candidate, token))
                {
                    Drop(mine.Copy);
                    held = null;
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error("search slot failed", e);
            fail(e);
        }
        finally
        {
            if (held is { } copy)
                Keep(copy);
        }
    }

    // True when the copy is as prepared again and may take the next candidate.
    private bool FightOne(
        SearchJob job,
        ISearchReport report,
        Copy copy,
        CopyUndo undo,
        string? baseSkill,
        Candidate candidate,
        CancellationToken token
    )
    {
        Interlocked.Increment(ref _fighting);
        try
        {
            TargetResult result;
            Note? left = null;
            string? skill = null;
            try
            {
                var changes = job.ChangesFor(candidate);
                if (!changes.IsEmpty)
                    copy.Change(
                        changes,
                        job.Options.IgnoreRequirements,
                        candidate.Ignoring.IsEmpty ? null : candidate.Ignoring.Select(c => c.Key).ToHashSet()
                    );
                skill = changes.IsEmpty ? baseSkill : copy.MainSkill;
                var refused = changes.IsEmpty ? null : copy.ChangeOutcomes?.FirstOrDefault(o => o.LeftOut);
                var eligibility = Targets.Check(copy.Game, job.Target);
                if (refused != null)
                {
                    left = refused.Reasons.FirstOrDefault() ?? Note.Of("{0} was refused", refused.Change.ShortForm);
                    result = Refused(job.Target, refused.Reasons.Count > 0 ? refused.Reasons : [left]);
                }
                else if (!eligibility.Eligible && !job.Options.IgnoreRequirements)
                {
                    left = eligibility.Reasons[0];
                    result = Refused(job.Target, eligibility.Reasons);
                }
                else
                {
                    var watch = Stopwatch.StartNew();
                    if (job.Target.Kind == TargetKind.Master)
                        result = MasterFight.Fight(
                            copy,
                            job.Target,
                            changes,
                            job.Options,
                            job.Seed,
                            token,
                            job.OwnLoadouts ? PartsFought.OwnLoadouts : PartsFought.Shared
                        );
                    else if (job.Target.Kind == TargetKind.Set)
                        result = Game.Search.SetFight.Fight(copy, job.Target, changes, job.Options, job.Seed, token);
                    else
                    {
                        copy.Reseed(job.Seed, job.Target.Key);
                        result = Checked(job, candidate, skill, out var checks) is { } reading
                            ? reading.Of(Driver.Fight(copy, job.Target, eligibility, job.Options, token, null, checks))
                            : Driver.Fight(copy, job.Target, eligibility, job.Options, token);
                    }
                    result.RealSeconds = watch.Elapsed.TotalSeconds;
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Error($"candidate {candidate.Index} failed", e);
                report.CandidateDone(candidate.Index, CopyFailures.Result(job.Target, e), null, skill);
                return false;
            }
            if (token.IsCancellationRequested)
                return false;
            report.CandidateDone(candidate.Index, result, left, skill);
            return Undo(undo, candidate);
        }
        finally
        {
            Interlocked.Decrement(ref _fighting);
        }
    }

    // A race fought once reads the set at its checks and stops it where the rule drops it.
    private static CheckReading? Checked(SearchJob job, Candidate candidate, string? skill, out DriveChecks? checks)
    {
        checks = null;
        if (job.Checks is not { } rule)
            return null;
        var reading = new CheckReading(rule, Checks.MetricOf(rule, skill, candidate.Style));
        checks = new DriveChecks(rule.At, reading.Reached);
        return reading;
    }

    private sealed class CheckReading(CheckRule rule, string metric)
    {
        private readonly List<TargetResult> _read = [];
        private bool _stopped;

        public bool Reached(int check, TargetResult result)
        {
            _read.Add(result);
            _stopped = Checks.At(rule, check, result, metric) != null;
            return _stopped;
        }

        // Every check read goes back with the fight. A set stopped at a check comes back as the fight it was, still
        // fighting and without its batches: its result is its last check's.
        public TargetResult Of(TargetResult fought)
        {
            if (_stopped)
                fought.Batches = [];
            fought.AtChecks = _read.Count > 0 ? _read : null;
            return fought;
        }
    }

    private bool Undo(CopyUndo undo, Candidate candidate)
    {
        IReadOnlyList<string> differing;
        try
        {
            differing = undo.Undo();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            differing = [$"{e.GetType().Name}: {e.Message}"];
        }
        if (differing.Count == 0)
            return true;
        Interlocked.Increment(ref _dropped);
        Log.Warn(
            $"a search copy didn't come back as prepared after candidate {candidate.Index} ({string.Join(", ", differing)}): a fresh copy fights next"
        );
        return false;
    }

    private static TargetResult Refused(TargetSpec target, IEnumerable<Note> reasons)
    {
        var result = new TargetResult
        {
            Key = target.Key,
            Kind = target.Kind,
            State = TargetState.Refused,
        };
        result.Reasons.AddRange(reasons);
        return result;
    }

    // A copy prepared with the base: one kept from an earlier stage, else from the stock, else built here once
    // memory allows. Null when the stage stopped first.
    private (Copy, CopyUndo, string?)? Acquire(SearchJob job, CancellationToken token)
    {
        lock (_gate)
        {
            if (_kept.TryPop(out var kept))
                return kept;
        }
        var copy = stock?.Invoke();
        if (copy != null)
            Interlocked.Increment(ref _fromStock);
        else
        {
            while (reserve != null && reserve.Decide(Live, preBuild: false) != ReserveDecision.Build)
            {
                if (Live == 0)
                    throw new NotEnoughMemoryException(reserve.OutOfMemoryReason());
                if (token.WaitHandle.WaitOne(200))
                    return null;
            }
            if (token.IsCancellationRequested)
                return null;
        }
        var live = Interlocked.Increment(ref _live);
        int peak;
        while ((peak = Volatile.Read(ref _peak)) < live && Interlocked.CompareExchange(ref _peak, live, peak) != peak)
        { }
        try
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            if (copy == null)
            {
                copy = Copy.Build();
                Interlocked.Increment(ref _built);
            }
            copy.Prepare(job.Snapshot, job.LoadoutSlot);
            copy.KeepPristine();
            var undo = CopyUndo.Of(copy);
            reserve?.Built(GC.GetAllocatedBytesForCurrentThread() - allocated);
            return (copy, undo, copy.MainSkill);
        }
        catch
        {
            copy?.Dispose();
            Interlocked.Decrement(ref _live);
            throw;
        }
    }

    private void Keep((Copy Copy, CopyUndo Undo, string? Skill) held)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _kept.Push(held);
                return;
            }
        }
        Drop(held.Copy);
    }

    private void Drop(Copy copy)
    {
        copy.Dispose();
        Interlocked.Decrement(ref _live);
    }
}
