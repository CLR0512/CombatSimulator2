namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// How a race fought once judges a set along its one fight: the counts it is read at (<see cref="At"/>, rising,
/// below the race's trials), the held set's results at those counts in the same order (<see cref="Held"/>, measured
/// in <see cref="HeldMetric"/>), the race's goal and share, and whether the set is judged as Droptimizer judges a
/// candidate against current gear (<see cref="Candidates"/>). With no held results the fight is only read, never
/// stopped: that is how the held set itself is fought. <see cref="Prices"/> travel only when the goal's metric
/// needs them. <see cref="CurrentSkill"/> and <see cref="CurrentStyle"/> measure a set as the race does: in current
/// gear's skill when it shares current gear's style.
/// </summary>
public sealed record CheckRule(
    IReadOnlyList<int> At,
    IReadOnlyList<TargetResult> Held,
    string HeldMetric,
    Goal Goal,
    double Share,
    bool Candidates = false,
    string? CurrentSkill = null,
    string? CurrentStyle = null,
    IReadOnlyDictionary<string, Price>? Prices = null
);

/// <summary>
/// The checks of a race fought once. A set's result at a check is the result a fight of that many trials on the
/// search's seed gives, so a check reads the numbers today's stage of that count reads; it is compared with the held
/// set's at the same count only, so whether it is dropped depends on the set and the held set alone, never on which
/// copy finished first.
/// </summary>
public static class Checks
{
    /// <summary>
    /// The counts a race of <paramref name="sets"/> sets over <paramref name="trials"/> trials checks at: its
    /// stages below the last.
    /// </summary>
    public static IReadOnlyList<int> Ladder(int trials, int sets) => [.. Answers.Stages(trials, sets).SkipLast(1)];

    /// <summary>
    /// Why a set leaves at check <paramref name="check"/> (its place in <see cref="CheckRule.At"/>), its result there
    /// <paramref name="result"/> measured in <paramref name="metric"/>; null when it fights on. The race's own rule
    /// on the held set and this one: clearly behind, over the death limit, or without a result to compare.
    /// </summary>
    public static DropReason? At(CheckRule rule, int check, TargetResult result, string metric)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (check < 0 || check >= rule.Held.Count)
            return null;
        IReadOnlyList<StageResult> pair =
        [
            new StageResult(0, rule.Held[check], rule.HeldMetric),
            new StageResult(1, result, metric),
        ];
        var prices = rule.Prices is { } given ? new PriceTable(given) : null;
        var dropped = rule.Candidates
            ? Droptimizer.After(pair, rule.Goal, rule.Share, prices).Dropped
            : Race.After(pair, rule.Goal, rule.Share, prices).Dropped;
        return dropped.TryGetValue(1, out var why) ? why : null;
    }

    /// <summary>The metric a set of <paramref name="style"/> training <paramref name="skill"/> is measured in.</summary>
    public static string MetricOf(CheckRule rule, string? skill, string? style)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return Answers.MetricFor(rule.Goal, skill, style, rule.CurrentSkill, rule.CurrentStyle);
    }

    /// <summary>Whether a goal's metric is read through prices, so a rule must carry them.</summary>
    public static bool NeedsPrices(Goal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return goal.Metric.StartsWith("value:", StringComparison.Ordinal)
            || goal.Metric.StartsWith("worth:", StringComparison.Ordinal);
    }
}
