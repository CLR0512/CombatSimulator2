using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Model.Slayer;

/// <summary>
/// One monster's pick: the loadout slot (null for the side's setup), its figure, its gain over the setup paired by
/// batch (null when the setup can't be compared: stuck, over the death limit, or the pick is the setup), the choices
/// within the pick's range (<see cref="Ties"/>), those left out for the death limit, and whether every choice gets the
/// master stuck on it.
/// </summary>
public sealed record LoadoutPick(
    string Monster,
    string? Choice,
    Estimate? Value,
    Estimate? Gain,
    IReadOnlyList<string?> Ties,
    IReadOnlyList<string?> OverLimit,
    bool Stuck
);

/// <summary>
/// Best loadouts' answer: each monster's pick, and the master's figure with the picks against the setup's for every
/// monster, paired by batch.
/// </summary>
public sealed record LoadoutPicksAnswer(
    string Master,
    Goal Goal,
    int Choices,
    IReadOnlyList<LoadoutPick> Picks,
    Estimate? Figure,
    Estimate? Gain
)
{
    /// <summary>The change that puts the picks on a side: the monsters not on the setup; null when every one is.</summary>
    public Change? Change =>
        Picks.Where(p => p.Choice != null).ToList() is { Count: > 0 } picked
            ? new Change(
                ChangeKind.Loadouts,
                Ids.MasterOf(Master),
                string.Join(',', picked.Select(p => p.Monster[(p.Monster.LastIndexOf('/') + 1)..] + "@" + p.Choice))
            )
            : null;
}

/// <summary>
/// The best saved loadout for each of a master's monsters, from the master fought once with the setup and once with
/// every monster on each saved loadout, all from one seed, so a monster's results pair by batch. Godot-free.
/// </summary>
public static class LoadoutPicks
{
    /// <summary>
    /// Picks per monster among the choices within the goal's death limit: a loadout over the setup only when its gain
    /// is wholly above zero, the best such by value; another choice not clearly behind the pick is a tie. With the
    /// setup stuck or over the limit, the best loadout that isn't. <paramref name="results"/> starts with the setup
    /// (choice null). Null when the setup's result has no monsters.
    /// </summary>
    public static LoadoutPicksAnswer? For(
        IReadOnlyList<(string? Choice, TargetResult Master)> results,
        Goal goal,
        TimeUnit unit = TimeUnit.Hour,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(goal);
        if (results.Count == 0 || MasterParts.Counted(results[0].Master) is not { Count: > 0 } setupParts)
            return null;
        // Blocked monsters are picked for too, so blocks chosen later can unblock one on its best loadout; the master's
        // figures are over the monsters it counts.
        var counted = setupParts.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var lower = Ranking.LowerIsBetter(goal.Metric);
        var picks = new List<LoadoutPick>();
        var picked = new List<TargetResult>();
        foreach (var monster in results[0].Master.Parts!.Select(p => p.Key))
        {
            var choices = results
                .Select(r => (r.Choice, Part: r.Master.Parts?.Find(p => p.Key == monster)))
                .Where(c => c.Part != null)
                .Select(c => (c.Choice, Part: c.Part!, Score: Score(c.Part!, goal.Metric, unit, prices)))
                .ToList();
            var overLimit = choices
                .Where(c => c.Score != null && !DeathLimits.Within(goal.Deaths, c.Part))
                .Select(c => c.Choice)
                .ToList();
            var valid = choices.Where(c => c.Score != null && !overLimit.Contains(c.Choice)).ToList();
            if (valid.Count == 0)
            {
                picks.Add(new LoadoutPick(monster, null, null, null, [], overLimit, Stuck: true));
                if (counted.Contains(monster))
                    picked.Add(choices[0].Part);
                continue;
            }
            var setup = valid.Find(c => c.Choice == null);
            var best =
                setup.Part != null
                    ? setup
                    : valid.OrderBy(c => lower ? c.Score!.Figure.Point : -c.Score!.Figure.Point).First();
            if (setup.Part != null)
            {
                var better = valid
                    .Where(c => c.Choice != null && Beyond(Paired(setup.Score!, c.Score!), lower))
                    .OrderBy(c => lower ? c.Score!.Figure.Point : -c.Score!.Figure.Point)
                    .ToList();
                if (better.Count > 0)
                    best = better[0];
            }
            var ties = valid
                .Where(c => c.Choice != best.Choice && !Behind(c.Score!, best.Score!, lower))
                .Select(c => c.Choice)
                .ToList();
            var gain =
                setup.Part != null && best.Choice != null
                    ? Differences.Percent(setup.Score!.Figure, best.Score!.Figure, Paired(setup.Score!, best.Score!))
                    : null;
            picks.Add(new LoadoutPick(monster, best.Choice, best.Score!.Figure, gain, ties, overLimit, Stuck: false));
            if (counted.Contains(monster))
                picked.Add(best.Part);
        }
        var figure = MasterFigures.Estimate(picked, goal.Metric, unit, prices);
        var setupSeries = MasterFigures.Series(setupParts, goal.Metric, unit, prices);
        var pickedSeries = MasterFigures.Series(picked, goal.Metric, unit, prices);
        var masterGain =
            figure != null
            && MasterFigures.Estimate(setupParts, goal.Metric, unit, prices) is { } setupFigure
            && setupSeries is { } a
            && pickedSeries is { } b
                ? Differences.Percent(
                    setupFigure,
                    figure,
                    Differences.Paired(a.Values, a.Denominators, b.Values, b.Denominators)
                )
                : null;
        return new LoadoutPicksAnswer(results[0].Master.Key, goal, results.Count, picks, figure, masterGain);
    }

    private sealed record Scored(Estimate Figure, double[] Values, double[] Times);

    // A monster's figure in the metric with its batch series; null when it is stuck (no kills).
    private static Scored? Score(TargetResult part, string metric, TimeUnit unit, PriceTable? prices) =>
        MasterFigures.Series([part], metric, unit, prices) is { } series
            ? new Scored(
                Estimators.RatioOfBatches(series.Values, series.Denominators),
                series.Values,
                series.Denominators
            )
            : null;

    // B − A paired by batch; null when the batches don't pair.
    private static Estimate? Paired(Scored a, Scored b) => Differences.Paired(a.Values, a.Times, b.Values, b.Times);

    // Whether a difference is clear of nothing on the better side.
    private static bool Beyond(Estimate? difference, bool lowerIsBetter) =>
        difference != null && (lowerIsBetter ? difference.High < 0 : difference.Low > 0);

    // Whether a choice is clearly behind the pick.
    private static bool Behind(Scored choice, Scored pick, bool lowerIsBetter) =>
        Beyond(Paired(choice, pick), lowerIsBetter);
}
