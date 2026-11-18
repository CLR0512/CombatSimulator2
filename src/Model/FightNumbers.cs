namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// How a target's fights went, as ratios of what the game reported, each over the batches with its 95% range like
/// every rate CS2 shows. Nothing here comes from stats: chance to hit is hits over attacks as they happened.
/// </summary>
public static class FightNumbers
{
    public const string Prefix = "fight:";
    public const string Hit = "fight:hit";
    public const string PerHit = "fight:perHit";
    public const string Dps = "fight:dps";
    public const string EnemyHit = "fight:enemyHit";
    public const string EnemyPerHit = "fight:enemyPerHit";
    public const string Taken = "fight:taken";
    public const string Overkill = "fight:overkill";
    public const string AttacksPerKill = "fight:attacks";
    public const string MissedPerKill = "fight:missed";
    public const string RanOver = "fight:ranOver";

    /// <summary>The numbers in the order they are shown.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [Hit, PerHit, Dps, EnemyHit, EnemyPerHit, Taken, Overkill, AttacksPerKill, MissedPerKill, RanOver];

    /// <summary>Whether less is better: what the enemy does to the player, and what a kill costs.</summary>
    public static bool LowerIsBetter(string key) =>
        key is EnemyHit or EnemyPerHit or Taken or Overkill or AttacksPerKill or MissedPerKill or RanOver;

    /// <summary>Whether the number is a share of something (written as a percentage), not a count or an amount.</summary>
    public static bool IsShare(string key) => key is Hit or EnemyHit or Overkill or RanOver;

    public static bool IsFightKey(string key) => key?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// The number's per-batch values and denominators; null when the result has no counts or nothing to measure
    /// (no attack, no hit, no kill). Ran over is a share of trials and has no series.
    /// </summary>
    public static (double[] Values, double[] Denominators)? Series(TargetResult result, string key)
    {
        ArgumentNullException.ThrowIfNull(result);
        // A master's numbers are its monsters', each weighted by the time its tasks take.
        if (result.Kind == TargetKind.Master)
            return key == RanOver ? null : Metrics.Series(result, key, TimeUnit.Hour);
        if (!result.HasFightCounts || key == RanOver)
            return null;
        if (Parts(key) is not { } parts)
            return null;
        var (value, denominator) = parts;
        var batches = result.Batches.Where(b => b.Fight != null).ToList();
        var values = batches.Select(b => value(b.Fight!, b)).ToArray();
        var denominators = batches.Select(b => denominator(b.Fight!, b)).ToArray();
        return denominators.Sum() > 0 ? (values, denominators) : null;
    }

    /// <summary>The number with its range; null when there is nothing to measure or no counts.</summary>
    public static Estimate? Estimate(TargetResult result, string key)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Kind == TargetKind.Master)
            return key == RanOver ? null : Metrics.Estimate(result, key, TimeUnit.Hour);
        if (key == RanOver)
        {
            if (!result.HasFightCounts)
                return null;
            var trials = result.Batches.Sum(b => b.Trials);
            return trials > 0 ? Estimators.Wilson(result.RanOver, trials) : null;
        }
        return Series(result, key) is { } series ? Estimators.RatioOfBatches(series.Values, series.Denominators) : null;
    }

    /// <summary>
    /// Damage per second from one cause of the player's (<paramref name="taken"/> false) or the enemy's, for the
    /// line under damage per second; null when there is none of it.
    /// </summary>
    public static Estimate? PerSecond(TargetResult result, Func<SideCounts, long> cause, bool taken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(cause);
        if (!result.HasFightCounts)
            return null;
        var batches = result.Batches.Where(b => b.Fight != null).ToList();
        var values = batches.Select(b => (double)cause(taken ? b.Fight!.Taken : b.Fight!.Dealt)).ToArray();
        var seconds = batches.Select(b => b.Fight!.Seconds).ToArray();
        return values.Sum() > 0 && seconds.Sum() > 0 ? Estimators.RatioOfBatches(values, seconds) : null;
    }

    /// <summary>The same numbers for the trials that ran over, as plain values; a key is missing when it has nothing to measure.</summary>
    public static IReadOnlyDictionary<string, double> OfStall(Stall stall)
    {
        ArgumentNullException.ThrowIfNull(stall);
        var tally = new Tally { Trials = stall.Trials, RanOver = stall.Trials };
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            if (key == RanOver || Parts(key) is not { } parts)
                continue;
            var (value, denominator) = parts;
            var d = denominator(stall.Counts, tally);
            if (d > 0)
                numbers[key] = value(stall.Counts, tally) / d;
        }
        return numbers;
    }

    private static (Func<FightCounts, Tally, double> Value, Func<FightCounts, Tally, double> Denominator)? Parts(
        string key
    ) =>
        key switch
        {
            Hit => ((c, _) => c.Dealt.Hits, (c, _) => c.Dealt.Attacks),
            PerHit => ((c, _) => c.Dealt.Attack, (c, _) => c.Dealt.Hits),
            Dps => ((c, _) => c.Dealt.Damage, (c, _) => c.Seconds),
            EnemyHit => ((c, _) => c.Taken.Hits, (c, _) => c.Taken.Attacks),
            EnemyPerHit => ((c, _) => c.Taken.Attack, (c, _) => c.Taken.Hits),
            Taken => ((c, _) => c.Taken.Damage, (c, _) => c.Seconds),
            Overkill => ((c, _) => c.Dealt.Overkill, (c, _) => c.Dealt.Damage + c.Dealt.Overkill),
            AttacksPerKill => ((c, _) => c.Dealt.Attacks, (_, t) => t.Kills),
            MissedPerKill => ((c, _) => c.Dealt.Attacks - c.Dealt.Hits, (_, t) => t.Kills),
            _ => null,
        };
}
