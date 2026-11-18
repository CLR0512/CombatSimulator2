using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// The parts of starting a run that read no game state: the run and its results, the order and the job.
/// </summary>
internal static partial class RunPlan
{
    /// <summary>
    /// A new running run for <paramref name="request"/>, every target queued under <paramref name="name"/>. The
    /// request's setup becomes the run's, with the snapshot's character.
    /// </summary>
    public static Run NewRun(
        RunRequest request,
        Snapshot snapshot,
        string id,
        ulong seed,
        string where,
        Func<TargetSpec, string> name
    )
    {
        var run = new Run
        {
            Id = id,
            Setup = request.Setup,
            SnapshotSha256 = snapshot.Sha256,
            GameBuild = snapshot.GameBuild,
            Checked = snapshot.Checked ? null : false,
            Seed = seed,
            Trials = request.Trials,
            Copies = request.Copies,
            SimulatedIn = where,
            Targets = [.. request.Targets.Select(t => t.Key)],
            State = RunState.Running,
            StartedAt = DateTime.UtcNow,
            SeedSet = request.Seed != null,
            Limits = request.Limits ?? new TrialLimits(),
            Fresh = request.Fresh,
            StopPrecision = request.StopPrecision,
            Timings = new RunTimings { Snapshot = snapshot.TakeMs / 1000 },
            ChangeOutcomes = request.ChangeOutcomes,
        };
        run.Setup.Character = snapshot.Character;
        foreach (var target in request.Targets)
        {
            run.Results[target.Key] = new TargetResult
            {
                Key = target.Key,
                Kind = target.Kind,
                Name = name(target),
                State = TargetState.Queued,
            };
        }
        return run;
    }

    /// <summary>
    /// The targets slowest first by earlier results, so no copy is left with a long one while others idle.
    /// </summary>
    public static IReadOnlyList<TargetSpec> LongestFirst(List<TargetSpec> simulate, ResultIndex? index)
    {
        if (index == null)
            return simulate;
        var byKey = simulate.GroupBy(t => t.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First());
        return [.. index.LongestFirst([.. simulate.Select(t => t.Key)]).Select(k => byKey[k])];
    }

    /// <summary>What the pool needs to fight <paramref name="ordered"/> for the request.</summary>
    public static PoolJob Job(
        RunRequest request,
        Snapshot snapshot,
        string? slot,
        IReadOnlyList<TargetSpec> ordered,
        ulong seed,
        TrialLimits limits,
        bool reuseCopies,
        string runId
    ) =>
        new(
            snapshot,
            slot,
            ordered,
            new DriveOptions(request.Trials, limits, request.Setup.IgnoreRequirements, request.StopPrecision),
            seed,
            request.Copies,
            reuseCopies,
            request.TargetSeeds,
            request.NextRunCopies,
            request.Setup.Changes,
            runId,
            request.Side,
            PartLoadouts(request.Setup, ordered)
        );
}
