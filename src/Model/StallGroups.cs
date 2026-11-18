namespace MelvorMods.CombatSimulator2.Model;

/// <summary>A row without an estimate whose trial ran over: its reason and the enemy's health left.</summary>
public sealed record StallMark(StallReason Reason, double HpLeftShare);

/// <summary>A reason heading of a ranking: the reason and how many of the rows shown have it.</summary>
public sealed record ReasonGroup(StallReason Reason, int Count);

/// <summary>
/// The rows that stalled, grouped by reason: listed after the targets where every fight died, by reason, then the
/// least enemy health left, then run order.
/// </summary>
public static class StallGroups
{
    /// <summary>The mark of a result with no estimate that has a stall; null otherwise, as for runs saved before.</summary>
    public static StallMark? MarkOf(TargetResult? result) =>
        result is { HasEstimates: false, Stall: { } stall }
            ? new StallMark(StallReasons.Of(stall), stall.HpLeftShare)
            : null;

    /// <summary>The share of trials that ran over, for a result with estimates and any; null otherwise.</summary>
    public static double? RanOverShare(TargetResult? result)
    {
        if (result is not { HasEstimates: true } || result.RanOver == 0)
            return null;
        var trials = result.Batches.Sum(b => b.Trials);
        return trials > 0 ? (double)result.RanOver / trials : null;
    }

    /// <summary>The stalled rows in their order; <paramref name="runOrder"/> gives each key's place in the run.</summary>
    public static IEnumerable<RankRow> Order(IEnumerable<RankRow> stalled, Func<string, int> runOrder) =>
        stalled.OrderBy(r => r.Stall!.Reason).ThenBy(r => r.Stall!.HpLeftShare).ThenBy(r => runOrder(r.Key));

    /// <summary>The reasons present among <paramref name="rows"/>, in order, each with its count.</summary>
    public static IReadOnlyList<ReasonGroup> Of(IEnumerable<RankRow> rows) =>
        [
            .. rows.Where(r => r.Stall != null)
                .GroupBy(r => r.Stall!.Reason)
                .OrderBy(g => g.Key)
                .Select(g => new ReasonGroup(g.Key, g.Count())),
        ];
}
