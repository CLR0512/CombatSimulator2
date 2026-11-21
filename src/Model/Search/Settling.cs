namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>What chose a slot's item in an answer.</summary>
public enum DecidedBy
{
    /// <summary>The search's goal told its items apart.</summary>
    Goal,

    /// <summary>The goal didn't; the chance of dying did.</summary>
    Deaths,

    /// <summary>Neither did; damage taken an hour did.</summary>
    DamageTaken,

    /// <summary>None of those did; food eaten an hour did.</summary>
    Food,

    /// <summary>Nothing told them apart: the tie order picked.</summary>
    Nothing,

    /// <summary>The search kept the slot as worn: it was locked.</summary>
    Locked,
}

/// <summary>
/// One option for a slot the goal couldn't tell from the finished set's item, fought in the finished set on the
/// settling's seed: its item (null for an empty slot), whether the character wears it there now or owns one, how
/// many requirements it lacks, its place in the game's item order, and whether it is the finished set's own.
/// </summary>
public sealed record SettleOption(
    string? Item,
    string? Name,
    bool Worn,
    bool Owned,
    int Unmet,
    int Order,
    TargetResult Result,
    bool Held = false
);

/// <summary>
/// One slot of an answer as decided: the item it holds (null for empty), whether it is worn now, its mark, what
/// decided it, and for a defensive measure the runner-up it beat and its difference from it (see
/// <see cref="Settling.Decide"/>).
/// </summary>
public sealed record SlotDecision(
    string Slot,
    string SlotName,
    string? Item,
    string? ItemName,
    bool Worn,
    Mark? Mark,
    DecidedBy DecidedBy,
    string? Against = null,
    Estimate? Difference = null
);

/// <summary>
/// Decides a slot the goal leaves tied. The goal is read first, at the settling's length; then the defensive
/// measures in a fixed order: the chance of dying, damage taken an hour, food eaten an hour, lower being better. At each, every option clearly worse than the best
/// (the 95% range of their paired difference excludes zero) drops out; the first measure that leaves one option
/// decides. Options still tied after all three go by the tie order: worn, then owned, then fewer unmet
/// requirements, then the search's own choice, then the game's order; the slot names the last measure that ruled any
/// out, or nothing when none did. Settling only among the goal's ties can't cost the goal.
/// </summary>
public static class Settling
{
    /// <summary>The measures in the order they decide, each with what it records.</summary>
    public static readonly IReadOnlyList<(string Metric, DecidedBy By)> Measures =
    [
        (Metrics.DeathRate, DecidedBy.Deaths),
        (FightNumbers.Taken, DecidedBy.DamageTaken),
        ("used:food", DecidedBy.Food),
    ];

    /// <summary>
    /// The option the slot keeps, what decided it, and for a defensive measure the runner-up and the winner's
    /// difference from it: relative, or the plain difference where the winner's is nothing (no deaths), as a percent
    /// of nothing isn't one. One option decides by nothing.
    /// </summary>
    public static (SettleOption Winner, DecidedBy By, SettleOption? Against, Estimate? Difference) Decide(
        IReadOnlyList<SettleOption> options,
        string goal,
        bool deathsFirst = false
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(goal);
        if (options.Count == 0)
            throw new ArgumentException("nothing to decide between", nameof(options));
        var left = options.ToList();
        // The last measure that ruled any option out: it decided between them and those, though the tie order then
        // picks among the options it left.
        (DecidedBy By, SettleOption Against, Estimate? Difference)? last = null;
        // The goal first, read again at the settling's length: a step's ties are only ties within its margin and
        // its trials, and settling must never cost the goal.
        // For the set closest to a death limit no set met, deaths come first and the goal next.
        (string Metric, DecidedBy By, bool Lower) byGoal = (goal, DecidedBy.Goal, Ranking.LowerIsBetter(goal));
        IEnumerable<(string Metric, DecidedBy By, bool Lower)> measures = deathsFirst
            ?
            [
                (Measures[0].Metric, Measures[0].By, true),
                byGoal,
                .. Measures.Skip(1).Select(m => (m.Metric, m.By, true)),
            ]
            : [byGoal, .. Measures.Select(m => (m.Metric, m.By, true))];
        foreach (var (metric, by, lower) in measures)
        {
            if (left.Count < 2)
                break;
            var values = left.Select(o => (Option: o, Value: Value(o, metric))).ToList();
            if (values.Any(v => v.Value == null))
                continue;
            var best = values.MinBy(v => lower ? v.Value!.Point : -v.Value!.Point).Option;
            var worse = left.Where(o => o != best && ClearlyWorse(best, o, metric, lower)).ToList();
            if (worse.Count == 0)
                continue;
            var runnerUp = worse.MinBy(o => lower ? Value(o, metric)!.Point : -Value(o, metric)!.Point)!;
            var (delta, percent) = Race.Difference(Stage(runnerUp, metric), Stage(best, metric));
            if (worse.Count == left.Count - 1)
                return (best, by, runnerUp, percent ?? delta);
            last = (by, runnerUp, percent ?? delta);
            left = [.. left.Except(worse)];
        }
        var picked = TieOrder(left)[0];
        return last is { } ruled
            ? (picked, ruled.By, ruled.Against, ruled.Difference)
            : (picked, DecidedBy.Nothing, null, null);
    }

    /// <summary>
    /// Options as the tie order ranks them: worn, owned, fewer unmet requirements, the search's own choice, the game's
    /// order.
    /// </summary>
    public static IReadOnlyList<SettleOption> TieOrder(IEnumerable<SettleOption> options) =>
        [
            .. options
                .OrderByDescending(o => o.Worn)
                .ThenByDescending(o => o.Owned)
                .ThenBy(o => o.Unmet)
                .ThenByDescending(o => o.Held)
                .ThenBy(o => o.Order),
        ];

    // Whether the other option is worse than the best on the measure beyond doubt: the paired difference's 95% range
    // lies wholly on the worse side of zero.
    private static bool ClearlyWorse(SettleOption best, SettleOption other, string metric, bool lower) =>
        Race.Difference(Stage(best, metric), Stage(other, metric)).Delta is { } delta
        && (lower ? delta.Low > 0 : delta.High < 0);

    private static Estimate? Value(SettleOption option, string metric) => Race.Value(Stage(option, metric));

    private static StageResult Stage(SettleOption option, string metric) => new(option.Order, option.Result, metric);
}
