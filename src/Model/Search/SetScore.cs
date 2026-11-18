using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>One member of a set search: its gain with the set over current gear, relative and paired by batch.</summary>
public sealed record MemberGain(string Key, Estimate? Gain);

/// <summary>A set's gain on each member of a set search, and what the answer says of them.</summary>
public static class SetScore
{
    /// <summary>
    /// The set's gain on each target it was scored over (each monster of a master, each target of an area or the
    /// pins) against current gear's, in order; empty for a result with one target.
    /// </summary>
    public static IReadOnlyList<MemberGain> PerMember(
        TargetResult current,
        TargetResult set,
        string metric,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(set);
        var before = Members(current).ToDictionary(m => m.Key, m => m.Result, StringComparer.Ordinal);
        return
        [
            .. Members(set)
                .Select(m => new MemberGain(
                    m.Key,
                    before.TryGetValue(m.Key, out var was) ? Gain(was, m.Result, metric, prices) : null
                )),
        ];
    }

    // A master's monsters by their own keys, a set's targets by theirs; nothing for one target.
    private static IEnumerable<(string Key, TargetResult Result)> Members(TargetResult result) =>
        result.Kind switch
        {
            TargetKind.Master => MasterParts.Counted(result).Select(p => (Ids.PartTarget(p.Key), p)),
            TargetKind.Set => (result.Parts ?? []).Select(p => (p.Key, p)),
            _ => [],
        };

    /// <summary>
    /// The targets of <paramref name="best"/> on which one of <paramref name="others"/> is better beyond the range of
    /// their difference, in the best's order.
    /// </summary>
    public static IReadOnlyList<string> WantsOwnSet(
        TargetResult best,
        IEnumerable<TargetResult> others,
        string metric,
        bool lowerIsBetter,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(best);
        ArgumentNullException.ThrowIfNull(others);
        var rivals = others
            .Select(o => Members(o).ToDictionary(m => m.Key, m => m.Result, StringComparer.Ordinal))
            .ToList();
        var wants = new List<string>();
        foreach (var (key, result) in Members(best))
        {
            if (Metrics.Estimate(result, metric, TimeUnit.Hour, prices) is not { } own)
                continue;
            var beaten = rivals.Any(r =>
                r.TryGetValue(key, out var other)
                && Metrics.Estimate(other, metric, TimeUnit.Hour, prices) is { } theirs
                && Differences.Independent(own, theirs) is var d
                && (lowerIsBetter ? d.High < 0 : d.Low > 0)
            );
            if (beaten)
                wants.Add(key);
        }
        return wants;
    }

    /// <summary>How many members the set is better on beyond the range.</summary>
    public static int Helps(IReadOnlyList<MemberGain> gains, bool lowerIsBetter) =>
        gains.Count(g => g.Gain is { } e && (lowerIsBetter ? e.High < 0 : e.Low > 0));

    /// <summary>The members the set is worse on beyond the range.</summary>
    public static IReadOnlyList<MemberGain> Worse(IReadOnlyList<MemberGain> gains, bool lowerIsBetter) =>
        [.. gains.Where(g => g.Gain is { } e && (lowerIsBetter ? e.Low > 0 : e.High < 0))];

    private static Estimate? Gain(TargetResult before, TargetResult after, string metric, PriceTable? prices)
    {
        if (
            Metrics.Estimate(before, metric, TimeUnit.Hour, prices) is not { } a
            || Metrics.Estimate(after, metric, TimeUnit.Hour, prices) is not { } b
        )
            return null;
        var paired =
            Metrics.Series(before, metric, TimeUnit.Hour, prices) is { } sa
            && Metrics.Series(after, metric, TimeUnit.Hour, prices) is { } sb
                ? Differences.Paired(sa.Values, sa.Denominators, sb.Values, sb.Denominators)
                : null;
        return Differences.Percent(a, b, paired);
    }
}
