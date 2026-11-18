namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>Why a set left a search.</summary>
public enum DropReason
{
    /// <summary>Clearly behind the leader, outside the margin.</summary>
    Behind,
    OverLimit,

    /// <summary>The game refused its gear or the target.</summary>
    Refused,
    Failed,

    /// <summary>Its fights didn't finish.</summary>
    Unfinished,
}

/// <summary>
/// One set's result of a stage. Index 0 is current gear. <see cref="Metric"/> is the goal's metric as this set
/// fought it: <c>xp:main</c> resolved to the set's own skill.
/// </summary>
public sealed record StageResult(int Index, TargetResult Result, string Metric);

/// <summary>
/// What a stage decided: the sets that go on, the sets dropped with why, the leader, and how far behind the leader
/// each set with a value was, as a share of the leader's value (0 for the leader, above 0 behind).
/// </summary>
public sealed record RaceStep(
    IReadOnlyList<int> In,
    IReadOnlyDictionary<int, DropReason> Dropped,
    int? Leader,
    IReadOnlyDictionary<int, double> Behind
);

/// <summary>A set as good as the best, with the 95% range of its difference from it.</summary>
public sealed record RaceTie(int Index, Estimate? FromBest);

/// <summary>
/// How a search ended. <see cref="Best"/> is current gear (0) under <see cref="AnswerState.KeepCurrent"/>, with the
/// sets as good as it in <see cref="Ties"/>. <see cref="Gain"/> and <see cref="Percent"/> are best − current.
/// <see cref="Closest"/> is set when no set is within the death limit.
/// </summary>
public sealed record RaceEnd(
    AnswerState State,
    int? Best,
    IReadOnlyList<RaceTie> Ties,
    Estimate? Gain = null,
    Estimate? Percent = null,
    int? Closest = null
);

/// <summary>
/// The rule a search races its sets by, on results fought from one seed so that batch i of one set pairs with
/// batch i of another. It only reads results: nothing here predicts a fight.
/// </summary>
public static class Race
{
    /// <summary>
    /// After a stage: who goes on. The leader is the best set by the metric among those inside the death limit so
    /// far. A set is dropped when it has no result to compare, is over the death limit, or is behind: the better end
    /// of the 95% range of its difference from the leader is worse than the leader by more than
    /// <paramref name="share"/> of the leader's value (at 0, worse at all). Current gear is never dropped here, so
    /// the gain can always be measured against it.
    /// </summary>
    public static RaceStep After(IReadOnlyList<StageResult> results, Goal goal, double share, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(goal);
        var lower = Ranking.LowerIsBetter(goal.Metric);
        var dropped = new Dictionary<int, DropReason>();
        var valued = new List<(StageResult Set, Estimate Value)>();
        foreach (var set in results)
        {
            if (Value(set, prices) is not { } value)
                dropped[set.Index] = set.Result.State switch
                {
                    TargetState.Refused => DropReason.Refused,
                    TargetState.Unfinished or TargetState.Finished => DropReason.Unfinished,
                    _ => DropReason.Failed,
                };
            else if (DeathLimits.Over(goal.Deaths, set.Result))
                dropped[set.Index] = DropReason.OverLimit;
            else
                valued.Add((set, value));
        }
        var behind = new Dictionary<int, double>();
        int? leader = null;
        if (valued.Count > 0)
        {
            var (lead, leadValue) = Best(valued, lower);
            leader = lead.Index;
            var scale = Math.Abs(leadValue.Point);
            foreach (var (set, value) in valued)
            {
                var worse = lower ? value.Point - leadValue.Point : leadValue.Point - value.Point;
                behind[set.Index] = scale > 0 ? worse / scale : worse;
                if (set.Index == lead.Index)
                    continue;
                // The most this set could be ahead of the leader, as an improvement: below the margin, it is behind.
                var delta = Delta(lead, set, leadValue, value, prices);
                var bestCase = lower ? -delta.Low : delta.High;
                if (bestCase < -share * scale)
                    dropped[set.Index] = DropReason.Behind;
            }
        }
        dropped.Remove(0);
        return new RaceStep(
            [.. results.Select(r => r.Index).Where(i => !dropped.ContainsKey(i))],
            dropped,
            leader,
            behind
        );
    }

    /// <summary>
    /// After the last stage, over the sets still in. The best is the best by the metric inside the death limit; a
    /// set is a tie when the 95% range of its difference from the best includes zero, or the difference is within
    /// <paramref name="share"/> of the best's value. Of the best and its ties, the answer is the one that takes the
    /// least damage; current gear is kept while it takes no more than that, within the share.
    /// </summary>
    public static RaceEnd End(IReadOnlyList<StageResult> results, Goal goal, double share, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(goal);
        var lower = Ranking.LowerIsBetter(goal.Metric);
        var valued = results
            .Select(set => (Set: set, Value: Value(set, prices)))
            .Where(p => p.Value != null)
            .Select(p => (p.Set, Value: p.Value!))
            .ToList();
        if (valued.Count == 0)
            return new RaceEnd(AnswerState.NoneKills, null, []);
        var within = valued.Where(p => DeathLimits.Within(goal.Deaths, p.Set.Result)).ToList();
        if (within.Count == 0)
            return new RaceEnd(
                AnswerState.NoneWithinLimit,
                null,
                [],
                Closest: DeathLimits.Closest(valued.Select(p => (p.Set.Index, p.Set.Result)))
            );
        var (best, bestValue) = Best(within, lower);
        var scale = Math.Abs(bestValue.Point);
        var group = new List<(StageResult Set, Estimate Value)> { (best, bestValue) };
        foreach (var (set, value) in within)
        {
            if (set.Index == best.Index)
                continue;
            var delta = Delta(best, set, bestValue, value, prices);
            if ((delta.Low <= 0 && delta.High >= 0) || Math.Abs(delta.Point) <= share * scale)
                group.Add((set, value));
        }
        var current = valued.FirstOrDefault(p => p.Set.Index == 0);
        var shown = Safest(group, share);
        var keep = shown.Set.Index == 0;
        var ties = group
            .Where(p => p.Set.Index != shown.Set.Index)
            .OrderBy(p => p.Set.Index)
            .Select(p => new RaceTie(p.Set.Index, Delta(shown.Set, p.Set, shown.Value, p.Value, prices)))
            .ToList();
        if (keep)
            return new RaceEnd(AnswerState.KeepCurrent, 0, ties);
        if (current.Set == null)
            return new RaceEnd(AnswerState.Found, shown.Set.Index, ties);
        var (gain, percent) = Difference(current.Set, shown.Set, prices);
        return new RaceEnd(AnswerState.Found, shown.Set.Index, ties, gain, percent);
    }

    // Of sets as good on the goal, the one that takes the least damage a second: an empty slot ties with armour when
    // armour only spares damage, and armour is the better answer. Current gear (index 0) stays, then the best, unless
    // the safest is clearly safer than it: its whole 95% range below the other's, and less by more than the share.
    // Damage near nothing is noise and never decides. Without damage counts, current gear stays when it is as good,
    // else the best.
    private static (StageResult Set, Estimate Value) Safest(List<(StageResult Set, Estimate Value)> group, double share)
    {
        var current = group.FirstOrDefault(p => p.Set.Index == 0);
        var best = group[0];
        var taken = group.Select(p => FightNumbers.Estimate(p.Set.Result, FightNumbers.Taken)).ToList();
        if (taken.Any(t => t == null))
            return current.Set != null ? current : best;
        var safest = taken.IndexOf(taken.MinBy(t => t!.Point));
        bool ClearlySafer(int than) =>
            taken[safest]!.High < taken[than]!.Low && taken[safest]!.Point < taken[than]!.Point * (1 - share);
        if (current.Set != null && !ClearlySafer(group.IndexOf(current)))
            return current;
        return ClearlySafer(0) ? group[safest] : best;
    }

    /// <summary>
    /// The first stage at which <paramref name="best"/> was behind that stage's leader by more than
    /// <paramref name="share"/>, counted from 1; null when it never was. A best that came from behind is fought
    /// again before it is shown. Stages before <paramref name="from"/> (from 0) aren't read: a short first stage
    /// is too noisy to say a best came from behind.
    /// </summary>
    public static int? BehindAt(IReadOnlyList<RaceStep> steps, int best, double share, int from = 0)
    {
        ArgumentNullException.ThrowIfNull(steps);
        for (var i = from; i < steps.Count; i++)
            if (steps[i].Behind.TryGetValue(best, out var behind) && behind > share)
                return i + 1;
        return null;
    }

    /// <summary>
    /// <paramref name="to"/> − <paramref name="from"/> in the goal's metric with its 95% range, and the same
    /// relative to <paramref name="from"/>; nulls where a side has no value.
    /// </summary>
    public static (Estimate? Delta, Estimate? Percent) Difference(
        StageResult from,
        StageResult to,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (Value(from, prices) is not { } a || Value(to, prices) is not { } b)
            return (null, null);
        var delta = Delta(from, to, a, b, prices);
        return (delta, Differences.Percent(a, b, delta));
    }

    /// <summary>The set's value in its metric per hour; null without a finite one.</summary>
    public static Estimate? Value(StageResult set, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(set);
        var value = Metrics.Estimate(set.Result, set.Metric, TimeUnit.Hour, prices);
        return value != null && double.IsFinite(value.Point) ? value : null;
    }

    /// <summary>
    /// The best set after <paramref name="best"/> by the goal's metric, among those <paramref name="other"/> allows
    /// inside the death limit, and no further behind it than <paramref name="within"/> of its value; null when there
    /// is none.
    /// </summary>
    public static int? RunnerUp(
        IReadOnlyList<StageResult> results,
        int best,
        Goal goal,
        double within,
        Func<int, bool> other,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(goal);
        var others = results
            .Where(set => set.Index != best && other(set.Index) && DeathLimits.Within(goal.Deaths, set.Result))
            .Select(set => (Set: set, Value: Value(set, prices)))
            .Where(p => p.Value != null)
            .Select(p => (p.Set, Value: p.Value!))
            .ToList();
        if (
            others.Count == 0
            || results.FirstOrDefault(r => r.Index == best) is not { } top
            || Value(top, prices) is not { } lead
        )
            return null;
        var lower = Ranking.LowerIsBetter(goal.Metric);
        var (second, value) = Best(others, lower);
        var behind = lower ? value.Point - lead.Point : lead.Point - value.Point;
        return behind <= within * Math.Abs(lead.Point) ? second.Index : null;
    }

    private static (StageResult Set, Estimate Value) Best(List<(StageResult Set, Estimate Value)> sets, bool lower) =>
        sets.OrderBy(p => lower ? p.Value.Point : -p.Value.Point).ThenBy(p => p.Set.Index).First();

    // to − from: paired on the batches where the metric is a ratio of batch sums, a proportion's own interval for
    // the death rate, and the two ranges taken as independent otherwise.
    private static Estimate Delta(StageResult from, StageResult to, Estimate a, Estimate b, PriceTable? prices)
    {
        if (from.Metric == Metrics.DeathRate)
            return Differences.Newcombe(a, b);
        if (
            Metrics.Series(from.Result, from.Metric, TimeUnit.Hour, prices) is { } sa
            && Metrics.Series(to.Result, to.Metric, TimeUnit.Hour, prices) is { } sb
            && Differences.Paired(sa.Values, sa.Denominators, sb.Values, sb.Denominators) is { } paired
        )
            return paired;
        return Differences.Independent(a, b);
    }
}
