namespace MelvorMods.CombatSimulator2.Model.Slayer;

/// <summary>A monster's share of a master's time.</summary>
public sealed record PartShare(string Key, double Share);

/// <summary>The monster a master gets stuck on, and why.</summary>
public sealed record StuckPart(string Key, Note Reason);

/// <summary>A master's deaths per time unit, and the monster most of them come from (null without deaths).</summary>
public sealed record MasterRisk(Estimate Deaths, string? MostFrom, long? HighestHit, long? LowestHp);

/// <summary>
/// A master's figures from its parts, one result per monster of its pool. The game draws a task's monster evenly
/// from the pool and its length from the same range whatever the monster, so over many tasks every monster is killed
/// equally often: a rate is the sum of each monster's value per kill over the sum of its time per kill. Each part's
/// batches are scaled by one over its kills and summed batch by batch, so every figure is a ratio of batch sums with
/// the range every other rate has.
/// </summary>
public static class MasterFigures
{
    /// <summary>
    /// The master's figure for one metric, with its range; null when any part has no kills (the master is stuck),
    /// for a death rate per trial (a master has no trials of its own), and for a metric no part has.
    /// </summary>
    public static Estimate? Estimate(
        IReadOnlyList<TargetResult> parts,
        string key,
        TimeUnit unit,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(key);
        if (parts.Count == 0 || Stuck(parts) != null || key == Metrics.DeathRate)
            return null;
        switch (key)
        {
            case Metrics.HighestHit:
            case Metrics.HighestReflect:
                return Extreme(parts.Select(p => Metrics.Estimate(p, key, unit, prices)), max: true);
            case Metrics.LowestHp:
                return Extreme(parts.Select(p => Metrics.Estimate(p, key, unit, prices)), max: false);
        }
        return Series(parts, key, unit, prices) is { } series
            ? Estimators.RatioOfBatches(series.Values, series.Denominators)
            : null;
    }

    /// <summary>
    /// The batch sums behind a master's ratio figure: each part's values and denominators over its kills, summed by
    /// batch over the batches every part has. Null as <see cref="Estimate"/> is, and for extremes.
    /// </summary>
    public static (double[] Values, double[] Denominators)? Series(
        IReadOnlyList<TargetResult> parts,
        string key,
        TimeUnit unit,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0 || Stuck(parts) != null)
            return null;
        var batches = parts.Min(p => p.Batches.Count);
        var values = new double[batches];
        var denominators = new double[batches];
        foreach (var part in parts)
        {
            if (Metrics.Series(part, key, unit, prices) is not { } series)
                return null;
            var kills = (double)part.Batches.Take(batches).Sum(b => b.Kills);
            if (kills == 0)
                return null;
            for (var i = 0; i < batches; i++)
            {
                values[i] += series.Values[i] / kills;
                denominators[i] += series.Denominators[i] / kills;
            }
        }
        return (values, denominators);
    }

    /// <summary>Each part's share of the master's time: its time per kill over the sum. Empty when stuck.</summary>
    public static IReadOnlyList<PartShare> Shares(IReadOnlyList<TargetResult> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0 || Stuck(parts) != null)
            return [];
        var perKill = parts.Select(p => p.Batches.Sum(b => b.GameSeconds) / p.Batches.Sum(b => b.Kills)).ToList();
        var total = perKill.Sum();
        return [.. parts.Select((p, i) => new PartShare(p.Key, perKill[i] / total))];
    }

    /// <summary>
    /// The first part with no kill, in pool order, with the reason: it never finished, every trial died, or every
    /// trial ran over the master's limit. Null when every part has kills.
    /// </summary>
    public static StuckPart? Stuck(IReadOnlyList<TargetResult> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        foreach (var part in parts)
        {
            if (part.HasEstimates && part.Batches.Sum(b => b.Kills) > 0)
                continue;
            if (!part.HasEstimates)
                return new StuckPart(
                    part.Key,
                    part.Reasons.Count > 0 ? part.Reasons[0] : Note.Of("no result for {0}", part.Name)
                );
            if (part.Stall is { } stall)
                return new StuckPart(part.Key, StallReasons.Note(stall, part.Name));
            // Translators: Why a Slayer master gets stuck on a monster: the player died in every fight with it.
            return new StuckPart(part.Key, Note.Of("dies every time"));
        }
        return null;
    }

    /// <summary>
    /// Deaths per <paramref name="unit"/> over the master's time, the part with the largest share of them, and the
    /// worst hit and lowest HP over every part. Null when stuck.
    /// </summary>
    public static MasterRisk? Risk(IReadOnlyList<TargetResult> parts, TimeUnit unit = TimeUnit.Hour)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (Estimate(parts, Metrics.Deaths, unit) is not { } deaths)
            return null;
        string? most = null;
        var mostPerKill = 0.0;
        foreach (var part in parts)
        {
            var perKill = part.Batches.Sum(b => b.Deaths) / (double)part.Batches.Sum(b => b.Kills);
            if (perKill > mostPerKill)
                (most, mostPerKill) = (part.Key, perKill);
        }
        var risks = parts.Select(Model.Risk.Of).Where(r => r is { Recorded: true }).Select(r => r!).ToList();
        return new MasterRisk(
            deaths,
            most,
            risks.Count > 0 ? risks.Max(r => r.HighestHit) : null,
            risks.Where(r => r.LowestHp != null).Select(r => r.LowestHp).Min()
        );
    }

    private static Estimate? Extreme(IEnumerable<Estimate?> estimates, bool max)
    {
        var known = estimates.Where(e => e != null).Select(e => e!).ToList();
        if (known.Count == 0)
            return null;
        return max ? known.MaxBy(e => e.Point) : known.MinBy(e => e.Point);
    }
}
