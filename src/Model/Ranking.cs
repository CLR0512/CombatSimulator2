namespace MelvorMods.CombatSimulator2.Model;

/// <summary>One target's place in a ranking by one metric.</summary>
/// <param name="Rank">1-based among the ranked targets: those with an estimate and a survived trial. Null for the
/// others.</param>
/// <param name="OfBest">Where it sits for a bar, 0…1 (1 is the best; see <see cref="Ranking.Position"/>); 0 when not
/// ranked.</param>
/// <param name="Reasons">Why there is no estimate (refused, cancelled, running…); null with one.</param>
/// <param name="Died">Every trial died: the estimate is kept and shown, but the target has no place.</param>
/// <param name="Stall">For a row without an estimate whose trial ran over: its reason; null otherwise.</param>
/// <param name="RanOver">For a row with an estimate: the share of its trials that ran over, null with none.</param>
public sealed record RankRow(
    string Key,
    string Name,
    int? Rank,
    Estimate? Estimate,
    double OfBest,
    TargetState State,
    IReadOnlyList<Note>? Reasons,
    bool Died = false,
    StallMark? Stall = null,
    double? RanOver = null
)
{
    public bool IsRunning => State is TargetState.Building or TargetState.Fighting;

    /// <summary>The reasons in English, for replies and tests.</summary>
    public string? Reason => Reasons?.English("; ");
}

/// <summary>
/// A run's targets by one metric: the ranked ones best first, then those where every trial died (by the metric),
/// then the targets without an estimate. Best, worst and median are the ranked targets', so a value earned while
/// dying sets no scale.
/// </summary>
/// <param name="Metric">The metric key ranked by, with <c>xp:main</c> resolved.</param>
public sealed record RankTable(
    string Metric,
    bool LowerIsBetter,
    IReadOnlyList<RankRow> Rows,
    double? Median,
    double? Best,
    double? Worst = null
)
{
    /// <summary>The number of the run's ranked targets, also when the rows shown are narrowed.</summary>
    public int Count => Ranked ?? Rows.Count(r => r.Rank != null);

    /// <summary>
    /// The run's best target: its first ranked row, whatever order or selection of rows is shown. One key also when
    /// values tie. Null when no target is ranked.
    /// </summary>
    public string? BestKey { get; init; }

    /// <summary>How many of the rows shown died every trial.</summary>
    public int DiedCount => Rows.Count(r => r.Died);

    /// <summary>The stall reasons among the rows shown, in order, each with its count.</summary>
    public IReadOnlyList<ReasonGroup> ReasonGroups => StallGroups.Of(Rows);

    // Set by Narrow: a narrowed table still counts the whole run, as its rows keep their places in it.
    private int? Ranked { get; init; }

    /// <summary>
    /// The rows of <paramref name="keys"/> only, as the target filter narrows what is shown; each keeps its rank and
    /// bar among all the run's targets, so a row reads the same as its details.
    /// </summary>
    public RankTable Narrow(IReadOnlySet<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return this with { Rows = [.. Rows.Where(r => keys.Contains(r.Key))], Ranked = Count };
    }
}

/// <summary>One metric of the selected target, against the run's other targets.</summary>
/// <param name="Rank">Its place, ties sharing the best of their places (two tied for first are both #1).</param>
/// <param name="MedianOfBest">Where the median sits on the same 0…1 scale as <paramref name="OfBest"/>.</param>
/// <param name="Spot">Where this target sits from the worst value of the run (0) to the best (1).</param>
/// <param name="Others">Where each other target with an estimate sits on the same scale.</param>
/// <param name="Died">Every trial of this target died: it has no place (<paramref name="Rank"/> is 0) and the
/// scale is the ranked targets' alone.</param>
public sealed record DetailRow(
    Metric Metric,
    Estimate Estimate,
    int Rank,
    int Count,
    double OfBest,
    double MedianOfBest,
    bool LowerIsBetter,
    double Spot,
    IReadOnlyList<double> Others,
    bool Died = false
)
{
    /// <summary>
    /// The middle half of the run's ranked targets (this one too, when it is ranked) and their median, on the same
    /// scale.
    /// </summary>
    public (double Low, double Median, double High) Box => Ranking.Quartiles(Died ? Others : [.. Others, Spot]);
}

/// <summary>One pinned target's value in a compare row.</summary>
/// <param name="OfBest">Fraction of the best pin, 0…1.</param>
/// <param name="DeltaPercent">How far behind the best pin, in percent (0 or less).</param>
/// <param name="Died">Every trial of this pin died: never best, and not measured against the best.</param>
public sealed record CompareCell(
    string Key,
    Estimate? Estimate,
    double OfBest,
    bool IsBest,
    double DeltaPercent,
    IReadOnlyList<Note>? Reasons,
    bool Died = false
)
{
    /// <summary>The reasons in English, for replies and tests.</summary>
    public string? Reason => Reasons?.English("; ");
}

public sealed record CompareRow(Metric Metric, bool LowerIsBetter, IReadOnlyList<CompareCell> Cells);

/// <summary>
/// Rank order, fraction of the best, median and pin comparisons: the one source for the list, chart, details and
/// compare views. Godot-free.
/// </summary>
public static partial class Ranking
{
    /// <summary>
    /// Whether lower is better: true for costs, time and deaths; every other metric is better higher.
    /// </summary>
    public static bool LowerIsBetter(string metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        return metric
                is Metrics.KillTime
                    or Metrics.DeathRate
                    or Metrics.Deaths
                    or Metrics.HighestHit
                    or Metrics.HighestReflect
            || FightNumbers.LowerIsBetter(metric)
            || metric.StartsWith("used:", StringComparison.Ordinal);
    }

    /// <summary>Every target in the run ranked by <paramref name="metric"/> (<c>xp:main</c> allowed).</summary>
    public static RankTable Rank(Run run, string metric, TimeUnit unit, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        var key = Metrics.Resolve(run, metric);
        var lower = LowerIsBetter(key);
        var survived = new List<(string Key, string Name, Estimate Estimate, TargetState State, double? RanOver)>();
        var died = new List<RankRow>();
        var stalled = new List<RankRow>();
        var without = new List<RankRow>();
        foreach (var target in run.Targets.Distinct(StringComparer.Ordinal))
        {
            var result = run.Results.GetValueOrDefault(target);
            var name = result?.Name is { Length: > 0 } n ? Spoilers.Current.Name(n) : target;
            var state = result?.State ?? TargetState.Queued;
            var estimate = result != null ? Metrics.Estimate(result, key, unit, prices) : null;
            if (estimate == null || double.IsNaN(estimate.Point))
            {
                var row = new RankRow(target, name, null, null, 0, state, ReasonOf(result, state))
                {
                    Stall = StallGroups.MarkOf(result),
                };
                (row.Stall != null ? stalled : without).Add(row);
            }
            else if (result!.NoneSurvived)
                died.Add(
                    new RankRow(target, name, null, estimate, 0, state, null, Died: true)
                    {
                        RanOver = StallGroups.RanOverShare(result),
                    }
                );
            else
                survived.Add((target, name, estimate, state, StallGroups.RanOverShare(result)));
        }

        double? best =
            survived.Count == 0 ? null
            : lower ? survived.Min(e => e.Estimate.Point)
            : survived.Max(e => e.Estimate.Point);
        var worst = Worst(survived.Select(e => e.Estimate.Point), lower);
        var rows = survived
            .Select(e => (e, ofBest: Position(e.Estimate.Point, best!.Value, worst, lower)))
            .OrderByDescending(p => p.ofBest)
            .ThenBy(p => p.e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.e.Key, StringComparer.Ordinal)
            .Select(
                (p, i) =>
                    new RankRow(p.e.Key, p.e.Name, i + 1, p.e.Estimate, p.ofBest, p.e.State, null)
                    {
                        RanOver = p.e.RanOver,
                    }
            )
            .ToList();
        var bestKey = rows.Count > 0 ? rows[0].Key : null;
        rows.AddRange(
            (lower ? died.OrderBy(r => r.Estimate!.Point) : died.OrderByDescending(r => r.Estimate!.Point))
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Key, StringComparer.Ordinal)
        );
        rows.AddRange(StallGroups.Order(stalled, k => run.Targets.IndexOf(k)));
        rows.AddRange(
            without
                .OrderBy(r => Group(r.State))
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Key, StringComparer.Ordinal)
        );
        return new RankTable(key, lower, rows, Median(survived.Select(e => e.Estimate.Point)), best, worst)
        {
            BestKey = bestKey,
        };
    }

    /// <summary>
    /// The order a ranking is shown in while its run is active: rows with a result already shown (in
    /// <paramref name="shown"/>, the previous order) keep their order, targets that got a result since follow them
    /// in ranking order, and the unranked rows come last as the ranking puts them (a target that died every trial
    /// never had a place to keep). Ranks stay the ranking's own. When <paramref name="final"/>, the ranking as it
    /// is: the view sorts once at the end.
    /// </summary>
    public static RankTable Hold(RankTable table, IReadOnlyList<string> shown, bool final = false)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(shown);
        if (final)
            return table;
        var ranked = table.Rows.Where(r => r.Rank != null).ToDictionary(r => r.Key, StringComparer.Ordinal);
        var rows = new List<RankRow>(table.Rows.Count);
        foreach (var key in shown)
        {
            if (ranked.Remove(key, out var row))
                rows.Add(row);
        }
        rows.AddRange(table.Rows.Where(r => r.Rank != null && ranked.ContainsKey(r.Key)));
        rows.AddRange(table.Rows.Where(r => r.Rank == null));
        return table with { Rows = rows };
    }

    /// <summary>
    /// Each metric of target <paramref name="target"/> against the run's other targets, in metric list order.
    /// Metrics that are zero for every target are left out, and with <paramref name="skipTargetsZeros"/> also any
    /// keyed metric (items, currencies, costs…) that is zero for the target. Empty when the target has no estimates.
    /// </summary>
    public static IReadOnlyList<DetailRow> Details(
        Run run,
        string target,
        TimeUnit unit,
        PriceTable? prices = null,
        Func<string, string>? names = null,
        bool skipTargetsZeros = false
    )
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Results.GetValueOrDefault(target) is not { HasFigures: true } own)
            return [];
        var details = new List<DetailRow>();
        foreach (var metric in Metrics.For(run.Results.Values, prices, names))
        {
            // Drops, costs and the like the target never has: left out before ranking, when asked.
            if (
                skipTargetsZeros
                && metric.Key.Contains(':', StringComparison.Ordinal)
                && Metrics.Estimate(own, metric.Key, unit, prices) is { Point: 0 }
            )
                continue;
            var table = Rank(run, metric.Key, unit, prices);
            var valued = table.Rows.Where(r => r.Estimate != null).ToList();
            if (valued.All(r => r.Estimate!.Point == 0))
                continue;
            if (valued.FirstOrDefault(r => r.Key == target) is not { } row)
                continue;
            var ranked = valued.Where(r => r.Rank != null).ToList();
            var median =
                table.Median is { } m && table.Best is { } b ? Position(m, b, table.Worst, table.LowerIsBetter) : 0;
            // Ties share a place: one more than the targets strictly better.
            var point = row.Estimate!.Point;
            var place = row.Died
                ? 0
                : 1 + ranked.Count(r => table.LowerIsBetter ? r.Estimate!.Point < point : r.Estimate!.Point > point);
            var points = ranked.Select(r => r.Estimate!.Point).Where(double.IsFinite).ToList();
            var best = table.LowerIsBetter ? points.DefaultIfEmpty(0).Min() : points.DefaultIfEmpty(0).Max();
            var worst = table.LowerIsBetter ? points.DefaultIfEmpty(0).Max() : points.DefaultIfEmpty(0).Min();
            details.Add(
                new DetailRow(
                    metric,
                    row.Estimate!,
                    place,
                    ranked.Count,
                    row.OfBest,
                    median,
                    table.LowerIsBetter,
                    // With nothing ranked there is no scale to sit on.
                    row.Died
                    && points.Count == 0
                        ? 0
                        : Span(point, best, worst),
                    [.. ranked.Where(r => r != row).Select(r => Span(r.Estimate!.Point, best, worst))],
                    row.Died
                )
            );
        }
        return details;
    }

    /// <summary>
    /// The pinned targets side by side: one row per metric present in any pinned result, one cell per pin in pin
    /// order. Ties for the best are all best. A pin without an estimate keeps its reason. A pin that died every
    /// trial keeps its values but is never best, and the others are measured against the best survivor.
    /// </summary>
    public static IReadOnlyList<CompareRow> Compare(
        Run run,
        IReadOnlyList<string> pins,
        TimeUnit unit,
        PriceTable? prices = null,
        Func<string, string>? names = null
    )
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(pins);
        var results = pins.Select(p => run.Results.GetValueOrDefault(p)).ToList();
        var rows = new List<CompareRow>();
        foreach (var metric in Metrics.For(results.OfType<TargetResult>(), prices, names))
        {
            var lower = LowerIsBetter(metric.Key);
            var estimates = results
                .Select(r => r != null ? Metrics.Estimate(r, metric.Key, unit, prices) : null)
                .Select(e => e != null && !double.IsNaN(e.Point) ? e : null)
                .ToList();
            var points = estimates
                .Where((e, i) => e != null && !results[i]!.NoneSurvived)
                .Select(e => e!.Point)
                .ToList();
            double? best =
                points.Count == 0 ? null
                : lower ? points.Min()
                : points.Max();
            var cells = new List<CompareCell>();
            for (var i = 0; i < pins.Count; i++)
            {
                var estimate = estimates[i];
                if (estimate == null)
                {
                    var result = results[i];
                    cells.Add(
                        new CompareCell(
                            pins[i],
                            null,
                            0,
                            false,
                            0,
                            ReasonOf(result, result?.State ?? TargetState.Queued)
                        )
                    );
                    continue;
                }
                if (results[i]!.NoneSurvived || best == null)
                {
                    cells.Add(new CompareCell(pins[i], estimate, 0, false, 0, null, results[i]!.NoneSurvived));
                    continue;
                }
                var ofBest = Position(estimate.Point, best.Value, Worst(points, lower), lower);
                var isBest = estimate.Point.Equals(best.Value);
                var delta = (OfBest(estimate.Point, best.Value, lower) - 1) * 100;
                cells.Add(new CompareCell(pins[i], estimate, ofBest, isBest, isBest ? 0 : delta, null));
            }
            rows.Add(new CompareRow(metric, lower, cells));
        }
        return rows;
    }

    /// <summary>
    /// How close <paramref name="value"/> is to <paramref name="best"/>, 0…1: value / best, or best / value when
    /// lower is better. Equal values are 1; anything not finite or without a positive best is 0.
    /// </summary>
    public static double OfBest(double value, double best, bool lowerIsBetter)
    {
        if (value.Equals(best))
            return double.IsFinite(value) ? 1 : 0;
        var fraction = lowerIsBetter ? best / value : value / best;
        return double.IsFinite(fraction) && fraction > 0 ? Math.Min(1, fraction) : 0;
    }

    /// <summary>The 25th, 50th and 75th percentiles, interpolated between values; zeros when there are none.</summary>
    public static (double Low, double Median, double High) Quartiles(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Where(double.IsFinite).Order().ToList();
        if (sorted.Count == 0)
            return (0, 0, 0);
        double At(double p)
        {
            var index = p * (sorted.Count - 1);
            var below = (int)Math.Floor(index);
            var above = Math.Min(below + 1, sorted.Count - 1);
            return sorted[below] + (sorted[above] - sorted[below]) * (index - below);
        }
        return (At(0.25), At(0.5), At(0.75));
    }

    /// <summary>
    /// Where a value sits between the run's worst (0) and best (1), whichever way is better; 1 when they are equal,
    /// 0 when not finite.
    /// </summary>
    public static double Span(double value, double best, double worst)
    {
        if (!double.IsFinite(value))
            return 0;
        if (best.Equals(worst))
            return 1;
        return Math.Clamp((value - worst) / (best - worst), 0, 1);
    }

    /// <summary>
    /// Where a value sits for a bar, 0…1 (1 is the best). Higher is better: value / best. Lower is better with a
    /// best above zero: best / value, the same proportion. Lower is better with a best of zero (no deaths), where
    /// that proportion is empty for everyone else: between <paramref name="worst"/> (0) and the best (1). Anything
    /// not finite is 0.
    /// </summary>
    public static double Position(double value, double best, double? worst, bool lowerIsBetter)
    {
        if (!lowerIsBetter || best > 0)
            return OfBest(value, best, lowerIsBetter);
        if (!double.IsFinite(value))
            return 0;
        if (value.Equals(best) || worst is not { } w || w <= best)
            return 1;
        return Math.Clamp((w - value) / (w - best), 0, 1);
    }

    // The worst finite value, for lower-is-better positions; null otherwise.
    private static double? Worst(IEnumerable<double> values, bool lowerIsBetter)
    {
        if (!lowerIsBetter)
            return null;
        var finite = values.Where(double.IsFinite).ToList();
        return finite.Count == 0 ? null : finite.Max();
    }

    /// <summary>The median of the values; null when there are none.</summary>
    public static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
            return null;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    // Targets without an estimate: running, then queued, then the final states.
    private static int Group(TargetState state) =>
        state switch
        {
            TargetState.Building or TargetState.Fighting => 0,
            TargetState.Queued => 1,
            TargetState.Finished or TargetState.Unfinished => 2,
            TargetState.Cancelled => 3,
            TargetState.Refused => 4,
            _ => 5,
        };

    /// <summary>Why a target has no estimate: its own reasons, else its state in words.</summary>
    internal static List<Note> ReasonOf(TargetResult? result, TargetState state)
    {
        if (result?.Reasons is { Count: > 0 } reasons)
            return reasons;
        if (Stuck(result) is { } stuck)
            return [stuck];
        return
        [
            state switch
            {
                TargetState.Queued => Note.Of("queued"),
                TargetState.Building or TargetState.Fighting => Note.Of("running"),
                TargetState.Finished => Note.Of("no estimate"),
                TargetState.Unfinished => Note.Of("unfinished"),
                TargetState.Cancelled => Note.Of("cancelled"),
                TargetState.Refused => Note.Of("refused"),
                TargetState.Unsupported => Note.Of("unsupported"),
                _ => Note.Text(state.ToString().ToLowerInvariant()),
            },
        ];
    }
}
