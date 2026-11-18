using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>Where a set left a replayed race: the trials it had fought and why.</summary>
internal sealed record ArmDrop(int Set, int At, string Why);

/// <summary>
/// One way of racing a round's sets, replayed on results fought ahead at every count it reads: what it would have
/// answered and how many trials it would have fought.
/// </summary>
internal sealed record ArmResult(
    string Arm,
    string State,
    int? Best,
    IReadOnlyList<int> Ties,
    double[]? Percent,
    long Fights,
    IReadOnlyDictionary<int, long> FightsAt,
    IReadOnlyList<ArmDrop> Drops,
    int? RerunFrom,
    int Disagreements = 0
);

/// <summary>
/// Replays a round's race several ways from results fought for every set at every count with one seed: today's
/// stages, where a set fights each stage again from its first trial and is dropped against the stage's leader, and
/// one continuous fight per set read at the same counts, dropped against the held set only. Every decision is the
/// search's own (<see cref="Race"/>); only what is compared and what it costs differ.
/// </summary>
internal sealed class LadderArms(
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, StageResult>> results,
    Goal goal,
    double share,
    PriceTable prices
)
{
    private IReadOnlyList<int> Sets => [.. results.Values.First().Keys.Order()];

    /// <summary>Today's race: each stage fought from the first trial, drops against the stage's leader.</summary>
    public ArmResult Stages(int trials)
    {
        var ladder = Answers.Stages(trials, Sets.Count);
        var inRace = Sets.ToList();
        var steps = new List<RaceStep>();
        var drops = new List<ArmDrop>();
        var at = new Dictionary<int, long>();
        long fights = 0;
        foreach (var count in ladder.SkipLast(1))
        {
            fights += (long)inRace.Count * count;
            at[count] = (long)inRace.Count * count;
            var step = Race.After(At(count, inRace), goal, share, prices);
            steps.Add(step);
            drops.AddRange(step.Dropped.Select(d => new ArmDrop(d.Key, count, d.Value.ToString())));
            inRace = [.. step.In];
        }
        fights += (long)inRace.Count * trials;
        at[trials] = (long)inRace.Count * trials;
        return Ended("stages", trials, inRace, steps, ladder, fights, at, drops);
    }

    /// <summary>
    /// One fight per set to the race's trials, read at every count below them in <paramref name="checks"/>: the
    /// held set fights to the end; any other leaves at the first check where it is clearly behind the held set or
    /// over the death limit, having cost the trials of that check. With <paramref name="leaderSteps"/>, the leader's
    /// gaps at each check are kept too, so a best that came from behind is still fought again.
    /// </summary>
    public ArmResult FoughtOnce(string arm, int trials, IReadOnlyList<int> checks, bool leaderSteps, double widen = 1)
    {
        var disagreements = 0;
        var drops = new List<ArmDrop>();
        var at = new Dictionary<int, long>();
        long fights = trials;
        var inRace = new List<int> { 0 };
        foreach (var set in Sets.Where(s => s != 0))
        {
            var dropped = false;
            foreach (var count in checks.Where(c => c < trials))
            {
                var step = Race.After(At(count, [0, set]), goal, share, prices);
                var why = Against(count, set, widen);
                if (widen == 1 && why != (step.Dropped.TryGetValue(set, out var raced) ? raced : null))
                    disagreements++;
                if (why == null)
                    continue;
                drops.Add(new ArmDrop(set, count, why.Value.ToString()));
                fights += count;
                at[count] = at.GetValueOrDefault(count) + count;
                dropped = true;
                break;
            }
            if (dropped)
                continue;
            fights += trials;
            inRace.Add(set);
        }
        at[trials] = (long)inRace.Count * trials;
        var steps = new List<RaceStep>();
        if (leaderSteps)
            foreach (var count in checks.Where(c => c < trials))
                steps.Add(Race.After(At(count, StillIn(count, drops)), goal, share, prices));
        return Ended(arm, trials, inRace, steps, checks, fights, at, drops) with { Disagreements = disagreements };
    }

    // The race's rule for one set against the held set at a check, with the range of their difference widened by
    // <paramref name="widen"/> about its point, as more checks must spend the same error between them. At 1 it is
    // Race.After with the two sets; the arms count where it isn't.
    private DropReason? Against(int count, int set, double widen)
    {
        if (!results[count].TryGetValue(set, out var other) || !results[count].TryGetValue(0, out var held))
            return DropReason.Failed;
        if (Race.Value(other, prices) is null)
            return other.Result.State switch
            {
                TargetState.Refused => DropReason.Refused,
                TargetState.Unfinished or TargetState.Finished => DropReason.Unfinished,
                _ => DropReason.Failed,
            };
        if (DeathLimits.Over(goal.Deaths, other.Result))
            return DropReason.OverLimit;
        if (Race.Value(held, prices) is not { } heldValue || DeathLimits.Over(goal.Deaths, held.Result))
            return null;
        var lower = Ranking.LowerIsBetter(goal.Metric);
        var value = Race.Value(other, prices)!;
        if (lower ? value.Point < heldValue.Point : value.Point > heldValue.Point)
            return null;
        var (delta, _) = Race.Difference(held, other, prices);
        if (delta == null)
            return null;
        var low = delta.Point - (delta.Point - delta.Low) * widen;
        var high = delta.Point + (delta.High - delta.Point) * widen;
        var bestCase = lower ? -low : high;
        return bestCase < -share * Math.Abs(heldValue.Point) ? DropReason.Behind : null;
    }

    /// <summary>The best and its ties over every set at <paramref name="trials"/>: the race's truth to score by.</summary>
    public RaceEnd Truth(int trials) => Race.End(At(trials, Sets), goal, share, prices);

    private List<int> StillIn(int count, List<ArmDrop> drops) =>
        [.. Sets.Where(s => !drops.Exists(d => d.Set == s && d.At < count))];

    private ArmResult Ended(
        string arm,
        int trials,
        List<int> inRace,
        List<RaceStep> steps,
        IReadOnlyList<int> ladder,
        long fights,
        Dictionary<int, long> at,
        List<ArmDrop> drops
    )
    {
        var end = Race.End(At(trials, inRace), goal, share, prices);
        int? rerun = null;
        if (end is { State: AnswerState.Found, Best: { } best } && ladder.Count >= 2)
        {
            rerun = Race.BehindAt(steps, best, share, from: ladder[0] == Answers.FirstRung ? 1 : 0);
            // Fought again on a second seed: current gear, the best and its ties, to the end.
            if (rerun != null)
                fights += (long)(2 + end.Ties.Count) * trials;
        }
        return new ArmResult(
            arm,
            end.State.ToString(),
            end.Best,
            [.. end.Ties.Select(t => t.Index)],
            end.Percent is { } p ? [p.Point, p.Low, p.High] : null,
            fights,
            at,
            drops,
            rerun
        );
    }

    private List<StageResult> At(int count, IEnumerable<int> sets) =>
        [.. sets.Where(results[count].ContainsKey).Select(s => results[count][s])];
}
