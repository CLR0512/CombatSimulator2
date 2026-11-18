namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>How many deaths a set may have and still be the answer.</summary>
public enum DeathLimit
{
    /// <summary>No trial may end in a death.</summary>
    None,

    /// <summary>Under 5% of trials may.</summary>
    UnderFivePercent,

    /// <summary>Any number, as long as one trial was survived.</summary>
    SurvivedOnce,
}

/// <summary>What a stat or modifier acts on, which decides whether it can reach a goal.</summary>
public enum PartGroup
{
    /// <summary>What the character does to the enemy.</summary>
    Fight,

    /// <summary>What the enemy does to the character.</summary>
    Defence,

    /// <summary>What a kill drops: items and currency.</summary>
    Rewards,

    /// <summary>The XP a fight gives.</summary>
    Xp,

    /// <summary>What a fight uses up: food, ammo, runes, charges.</summary>
    Costs,

    /// <summary>Anything else: taken to reach every goal.</summary>
    Other,
}

/// <summary>What a search looks for: the best of one metric ("Rank by" keys), within a death limit.</summary>
public sealed record Goal(string Metric, DeathLimit Deaths = DeathLimit.None)
{
    /// <summary>
    /// Whether a stat of <paramref name="group"/> can change this goal's result, so that two items may be compared
    /// without it when it can't. The fight always can. Defence can whenever the target attacks, whatever the metric:
    /// it decides deaths and what is eaten. Rewards reach only what a kill drops, XP gains only XP, costs only what
    /// is used.
    /// </summary>
    public bool Counts(PartGroup group, bool targetAttacks) =>
        group switch
        {
            PartGroup.Fight => true,
            PartGroup.Defence => targetAttacks,
            PartGroup.Rewards => IsReward(Metric) && !IsXp(Metric),
            PartGroup.Xp => IsXp(Metric),
            PartGroup.Costs => Metric.StartsWith("used:", StringComparison.Ordinal),
            _ => true,
        };

    /// <summary>
    /// Whether items and totals may be compared for <paramref name="metric"/>: only where pairs of sets fought in
    /// the game showed that more of a counted stat, in the game's direction, never gives a clearly worse result.
    /// That holds for kills, time per kill, XP and what kills give. It is not known for what a fight uses up (a
    /// quicker fight can eat more an hour), for deaths as the thing ranked, for the fight's own numbers, or for the
    /// extremes, which are one trial's value: for those, and for any metric not named here, every set is fought.
    /// </summary>
    public static bool AllowsComparing(string metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        return metric is Metrics.Kills or Metrics.KillTime || IsReward(metric);
    }

    private static bool IsXp(string metric) => metric.StartsWith("xp:", StringComparison.Ordinal);

    // What a kill gives, XP included.
    private static bool IsReward(string metric)
    {
        if (metric == Metrics.Drops)
            return true;
        var colon = metric.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && metric[..colon] is "xp" or "item" or "currency" or "value" or "worth" or "bury" or "rare";
    }
}
