using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

// A comparison runs its two sides one after the other, side B from side A's snapshot and seeds.
internal sealed partial class RunService
{
    // Side B waits for side A to finish: its request (A's, with B's setup) and A's snapshot, so both sides start
    // from the same character.
    private sealed record PendingB(RunRequest Request, Snapshot Snapshot, bool SeedSet);

    private PendingB? _pendingB;

    /// <summary>
    /// Compares setup <paramref name="b"/> with <paramref name="a"/>'s setup on <paramref name="a"/>'s targets:
    /// one snapshot for both; side A runs first (reusing earlier results as a single run would), then side B, which
    /// reuses or fights each target with the seed A's result came from, so both sides of a target share a seed.
    /// </summary>
    public ComparedRuns StartComparison(RunRequest a, Setup b) => StartComparison(a, b, null);

    // With outcomes, side B has changes, checked as given; side A's are on its request.
    private ComparedRuns StartComparison(RunRequest a, Setup b, List<ChangeOutcome>? outcomes)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        MainThread.Assert("RunService.StartComparison");
        if (IsActive)
            throw new CommandException($"{Current!.Id} is still running: cancel it first (cs2 cancel)");
        var game = LiveGame.Require();
        CheckLoadout(b, () => LiveGame.Loadouts(game)); // refuse a missing or locked B before A runs
        var snapshot = LiveGame.TakeSnapshot(game);
        // Side B starts the moment A ends: the host builds its copies while A fights.
        var runA = Start(a with { NextRunCopies = a.Copies, Side = 'A' }, snapshot);
        var record = new Comparison
        {
            Kind = Model.Comparison.KindOf(a.Setup, b),
            A = runA.Id,
            Targets = [.. runA.Targets],
            Seed = runA.Seed,
            SeedSet = runA.SeedSet,
            CreatedAt = DateTime.UtcNow,
            Cs2Mvid = Cs2Mvid,
        };
        b.Character = runA.Setup.Character;
        var placeholder = ComparedRuns.Placeholder(runA, b);
        placeholder.ChangeOutcomes = outcomes;
        Comparison = new ComparedRuns(record, runA, placeholder, []);
        _pendingB = new PendingB(
            a with
            {
                Setup = b,
                Seed = runA.Seed,
                NextRunCopies = 0,
                ChangeOutcomes = outcomes,
            },
            snapshot,
            runA.SeedSet
        );
        Log.Info($"comparison of {runA.Setup.Identity} and {b.Identity} started, side A {runA.Id}");
        if (runA.IsFinal)
            StartSideB(); // A answered entirely from earlier runs
        return Comparison;
    }

    /// <summary>
    /// Compares <paramref name="now"/>'s setup as it is now with a saved run: the saved run is side A, unchanged;
    /// side B runs on its targets with its trials, limits, early stop and each target's seed.
    /// </summary>
    public ComparedRuns StartAgainst(Run baseline, RunRequest now)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(now);
        MainThread.Assert("RunService.StartAgainst");
        if (IsActive)
            throw new CommandException($"{Current!.Id} is still running: cancel it first (cs2 cancel)");
        if (baseline.Targets.Count == 0)
            throw new CommandException($"{baseline.Id} has no targets");
        var request = now with
        {
            Targets = [.. baseline.Targets.Select(TargetSpec.FromKey)],
            Trials = baseline.Trials,
            Seed = baseline.Seed,
            Limits = baseline.Limits,
            StopPrecision = baseline.StopPrecision,
            TargetSeeds = PairSeeds.Of(baseline),
        };
        var runB = Start(request with { Side = 'B' });
        runB.SeedSet = baseline.SeedSet;
        var record = new Comparison
        {
            Id = Model.Comparison.IdFor(runB.Id),
            Kind = ComparisonKind.NowAgainstSaved,
            A = baseline.Id,
            B = runB.Id,
            Targets = [.. baseline.Targets],
            Seed = baseline.Seed,
            SeedSet = baseline.SeedSet,
            CreatedAt = DateTime.UtcNow,
            Cs2Mvid = Cs2Mvid,
        };
        Comparison = new ComparedRuns(record, baseline, runB, ComparisonWarnings.Of(baseline, runB, Cs2Mvid));
        Log.Info($"comparison of {runB.Setup.Identity} now against {baseline.Id} started, side B {runB.Id}");
        if (runB.IsFinal)
            ComparisonDone(Comparison);
        return Comparison;
    }

    // Side A is final: start side B with A's per-target seeds, or end the comparison if A didn't finish.
    private void StartSideB()
    {
        if (_pendingB is not { } pending || Comparison is not { } comparison || !comparison.A.IsFinal)
            return;
        _pendingB = null;
        var a = comparison.A;
        if (a.State != RunState.Finished)
        {
            ComparedRuns.EndPlaceholder(comparison.B, a.Error ?? Note.Of("cancelled"), a.State);
            _changed = true;
            ComparisonDone(comparison);
            return;
        }
        Run runB;
        try
        {
            runB = Start(pending.Request with { TargetSeeds = PairSeeds.Of(a), Side = 'B' }, pending.Snapshot);
        }
        catch (CommandException e)
        {
            Log.Warn("side B not started", e);
            ComparedRuns.EndPlaceholder(comparison.B, Note.Text(e.Message), RunState.Error);
            comparison.B.Error = Note.Text(e.Message);
            _changed = true;
            ComparisonDone(comparison);
            return;
        }
        runB.SeedSet = pending.SeedSet;
        comparison.Record.B = runB.Id;
        comparison.Record.Id = Model.Comparison.IdFor(runB.Id);
        Comparison = comparison with { B = runB };
        Log.Info($"comparison side B {runB.Id} started");
        if (runB.IsFinal)
            ComparisonDone(Comparison);
    }

    /// <summary>Main thread: shows the finished comparison with its sides exchanged, and saves it so, fighting no more.</summary>
    public bool SwapComparison()
    {
        if (Comparison is not { CanSwap: true } shown)
            return false;
        ComparisonDone(Comparison = shown.Swapped(Cs2Mvid));
        return true;
    }

    private void ComparisonDone(ComparedRuns comparison)
    {
        Compared?.Invoke(comparison);
        _changed = true;
        Log.Info(
            $"comparison {comparison.Record.Id} {comparison.State.ToString().ToLowerInvariant()}: A {comparison.A.Id}, B {comparison.B.Id}"
        );
        Keeper?.Keep(comparison);
    }
}
