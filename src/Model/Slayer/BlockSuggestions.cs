namespace MelvorMods.CombatSimulator2.Model.Slayer;

/// <summary>
/// One choice of a master's blocks: the enemies blocked (target keys), the master's figure with them, its gain over
/// the current blocks (relative, paired by batch; null when the current blocks get stuck), the slots it needs past
/// those open and the tasks still needed to open the last of them, what the blocks not already made cost and the hours
/// of the master's own coins that takes, and whether no monster left had a death.
/// </summary>
public sealed record BlockChoice(
    IReadOnlyList<string> Blocks,
    Estimate Figure,
    Estimate? Gain,
    int SlotsNeeded,
    int TasksNeeded,
    long Cost,
    double? Hours,
    bool SafeToLeave
);

/// <summary>
/// The blocks to suggest for a master: the best within the slots open now, and a better one needing a slot not yet
/// open when it is better beyond its range. <see cref="AsGood"/> when no choice within the open slots gains beyond its
/// range over the current blocks.
/// </summary>
public sealed record BlockSuggestion(
    string Metric,
    IReadOnlyList<string> Current,
    int Choices,
    BlockChoice? Best,
    BlockChoice? NeedsSlot,
    bool AsGood
);

/// <summary>
/// Block suggestions from a master's result, no fights: its run fought every monster it can give, the blocked ones
/// too, so each choice of none, one or two blocks is its figures over the monsters left.
/// </summary>
public static class BlockSuggestions
{
    public static BlockSuggestion? For(
        TargetResult master,
        string metric,
        TimeUnit unit,
        PriceTable? prices = null,
        bool lowerIsBetter = false
    )
    {
        ArgumentNullException.ThrowIfNull(master);
        if (master is not { State: TargetState.Finished, Parts: { Count: > 0 } parts, Master: { } facts })
            return null;
        var keys = parts.Select(p => Ids.PartTarget(p.Key)).ToList();
        var current = (master.Blocked ?? []).Order(StringComparer.Ordinal).ToList();
        var baseline = Scored(parts, keys, current, metric, unit, prices);
        var choices = new List<BlockChoice>();
        var count = 0;
        foreach (var blocks in Subsets(keys, facts.Slots.Count))
        {
            count++;
            if (Scored(parts, keys, blocks, metric, unit, prices) is not { } scored)
                continue;
            var gain = baseline is { } b
                ? Differences.Percent(
                    b.Figure,
                    scored.Figure,
                    Differences.Paired(b.Values, b.Times, scored.Values, scored.Times)
                )
                : null;
            var needed = Math.Max(0, blocks.Count - facts.OpenSlots);
            var added = blocks.Count(k => !current.Contains(k));
            var cost = facts.BlockCost * added;
            var coins = MasterFigures.Estimate(scored.Left, "currency:" + facts.Currency, TimeUnit.Hour, prices)?.Point;
            choices.Add(
                new BlockChoice(
                    blocks,
                    scored.Figure,
                    gain,
                    needed,
                    needed > 0 ? facts.Slots[blocks.Count - 1].TasksNeeded : 0,
                    cost,
                    cost == 0 ? 0
                        : coins > 0 ? cost / coins
                        : null,
                    scored.Left.TrueForAll(p => p.Batches.Sum(t => t.Deaths) == 0)
                )
            );
        }
        int Order(BlockChoice a, BlockChoice b) =>
            lowerIsBetter ? a.Figure.Point.CompareTo(b.Figure.Point) : b.Figure.Point.CompareTo(a.Figure.Point);
        var open = choices.Where(c => c.SlotsNeeded == 0).ToList();
        open.Sort(Order);
        var best = open.FirstOrDefault();
        var asGood = baseline != null && (best == null || !Beyond(best.Gain, lowerIsBetter));
        var all = choices.ToList();
        all.Sort(Order);
        var needsSlot =
            all.FirstOrDefault() is { SlotsNeeded: > 0 } top
            && (best == null || BetterThan(top, best, parts, keys, metric, unit, prices, lowerIsBetter))
                ? top
                : null;
        return new BlockSuggestion(metric, current, count, best, needsSlot, asGood);
    }

    // Whether a gain is clear of nothing: the whole range on the better side.
    private static bool Beyond(Estimate? gain, bool lowerIsBetter) =>
        gain != null && (lowerIsBetter ? gain.High < 0 : gain.Low > 0);

    // Whether one choice is better than another beyond the range of their paired difference.
    private static bool BetterThan(
        BlockChoice candidate,
        BlockChoice than,
        List<TargetResult> parts,
        List<string> keys,
        string metric,
        TimeUnit unit,
        PriceTable? prices,
        bool lowerIsBetter
    )
    {
        var a = Scored(parts, keys, than.Blocks, metric, unit, prices)!;
        var b = Scored(parts, keys, candidate.Blocks, metric, unit, prices)!;
        return Beyond(Differences.Paired(a.Values, a.Times, b.Values, b.Times), lowerIsBetter);
    }

    private sealed record Score(Estimate Figure, double[] Values, double[] Times, List<TargetResult> Left);

    // The master's figure with these blocks; null when nothing is left or a monster left gets the master stuck.
    private static Score? Scored(
        List<TargetResult> parts,
        List<string> keys,
        IReadOnlyCollection<string> blocks,
        string metric,
        TimeUnit unit,
        PriceTable? prices
    )
    {
        var left = parts.Where((_, i) => !blocks.Contains(keys[i])).ToList();
        if (left.Count == 0 || MasterFigures.Series(left, metric, unit, prices) is not { } series)
            return null;
        return new Score(
            Estimators.RatioOfBatches(series.Values, series.Denominators),
            series.Values,
            series.Denominators,
            left
        );
    }

    // Every choice of up to `most` of the keys, none first, each in key order.
    private static IEnumerable<List<string>> Subsets(List<string> keys, int most)
    {
        yield return [];
        if (most >= 1)
            foreach (var key in keys)
                yield return [key];
        if (most >= 2)
            for (var i = 0; i < keys.Count; i++)
            for (var j = i + 1; j < keys.Count; j++)
                yield return [.. new[] { keys[i], keys[j] }.Order(StringComparer.Ordinal)];
    }
}
