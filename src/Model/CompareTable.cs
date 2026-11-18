namespace MelvorMods.CombatSimulator2.Model;

/// <summary>What a comparison's list is ranked by.</summary>
public enum CompareOrder
{
    /// <summary>How much better B is, best first.</summary>
    Difference,

    /// <summary>Side A's value, best first.</summary>
    A,

    /// <summary>Side B's value, best first.</summary>
    B,
}

/// <summary>Which targets a comparison's list shows.</summary>
public enum CompareFilter
{
    All,
    Better,
    Worse,
}

/// <summary>
/// One target's row: its difference and its place in the order (null when it has nothing to rank by).
/// </summary>
public sealed record CompareTableRow(Difference Difference, int? Rank)
{
    public string Key => Difference.Key;
}

/// <summary>
/// A comparison's targets by one metric, ordered and filtered; the counts cover every target before filtering.
/// </summary>
/// <param name="Metric">The metric key, with <c>xp:main</c> resolved on side A.</param>
public sealed record CompareTable(
    string Metric,
    bool LowerIsBetter,
    IReadOnlyList<CompareTableRow> Rows,
    int Better,
    int Worse,
    int NoClearDifference,
    int NoDifference
)
{
    /// <summary>
    /// The order a table is shown in while its comparison runs: rows already ranked keep their place, newly ranked
    /// ones follow in order, unranked rows last. When <paramref name="final"/>, the table as it is.
    /// </summary>
    public CompareTable Hold(IReadOnlyList<string> shown, bool final = false)
    {
        ArgumentNullException.ThrowIfNull(shown);
        if (final)
            return this;
        var ranked = Rows.Where(r => r.Rank != null).ToDictionary(r => r.Key, StringComparer.Ordinal);
        var rows = new List<CompareTableRow>(Rows.Count);
        foreach (var key in shown)
        {
            if (ranked.Remove(key, out var row))
                rows.Add(row);
        }
        rows.AddRange(Rows.Where(r => r.Rank != null && ranked.ContainsKey(r.Key)));
        rows.AddRange(Rows.Where(r => r.Rank == null));
        return this with { Rows = rows };
    }
}

public static class CompareTables
{
    /// <summary>The targets of <paramref name="a"/> compared with <paramref name="b"/>, ordered and filtered.</summary>
    public static CompareTable Of(
        Run a,
        Run b,
        string metric,
        TimeUnit unit,
        PriceTable? prices = null,
        CompareOrder order = CompareOrder.Difference,
        CompareFilter filter = CompareFilter.All
    )
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var differences = Differences.Of(a, b, metric, unit, prices);
        var lower = Ranking.LowerIsBetter(Metrics.Resolve(a, metric));
        var counts = differences.GroupBy(d => d.Verdict).ToDictionary(g => g.Key, g => g.Count());
        double? Key(Difference d) =>
            order switch
            {
                CompareOrder.Difference => double.IsNaN(d.Improvement) ? null : d.Improvement,
                CompareOrder.A => Signed(d.A, lower),
                _ => Signed(d.B, lower),
            };
        var ranked = differences
            .Select(d => (d, key: Key(d)))
            .Where(p => p.key != null)
            .OrderByDescending(p => p.key)
            .ThenBy(p => p.d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.d.Key, StringComparer.Ordinal)
            .Select((p, i) => new CompareTableRow(p.d, i + 1));
        var unranked = differences
            .Where(d => Key(d) == null)
            .OrderBy(d => Group(d))
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Key, StringComparer.Ordinal)
            .Select(d => new CompareTableRow(d, null));
        var rows = ranked
            .Concat(unranked)
            .Where(r =>
                filter switch
                {
                    CompareFilter.Better => r.Difference.Verdict == Verdict.Better,
                    CompareFilter.Worse => r.Difference.Verdict == Verdict.Worse,
                    _ => true,
                }
            )
            .ToList();
        return new CompareTable(
            Metrics.Resolve(a, metric),
            lower,
            rows,
            counts.GetValueOrDefault(Verdict.Better),
            counts.GetValueOrDefault(Verdict.Worse),
            counts.GetValueOrDefault(Verdict.NoClearDifference),
            counts.GetValueOrDefault(Verdict.None)
        );
    }

    // A side's value signed so that higher is better; null without a finite estimate.
    private static double? Signed(Estimate? e, bool lowerIsBetter) =>
        e == null || !double.IsFinite(e.Point) ? null
        : lowerIsBetter ? -e.Point
        : e.Point;

    // Targets still running first, then those waiting, then the ones that ended without a difference.
    private static int Group(Difference d)
    {
        var running =
            d.StateA is TargetState.Building or TargetState.Fighting
            || d.StateB is TargetState.Building or TargetState.Fighting;
        if (running)
            return 0;
        if (d.StateA == TargetState.Queued || d.StateB == TargetState.Queued)
            return 1;
        return d.Delta != null ? 2 : 3;
    }
}
