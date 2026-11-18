namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// How close a target came to killing the player, over all its trials. <see cref="Recorded"/> is false for results
/// saved before risk was kept; the other values are then absent. <see cref="LowestHp"/> covers survived trials only,
/// so null means every trial died. The extremes have no range of their own; the close-call share gives the read-out
/// its uncertainty.
/// </summary>
public sealed record Risk(bool Recorded, long HighestHit, long? LowestHp, Estimate? CloseCalls)
{
    /// <summary>Whether the highest hit taken is at least the lowest HP reached, so one such hit could kill.</summary>
    public bool Warning => Recorded && LowestHp is { } lowest && HighestHit >= lowest;

    /// <summary>The target's risk, or null for a result without estimates.</summary>
    public static Risk? Of(TargetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.HasEstimates)
            return null;
        long? highest = null;
        long? lowest = null;
        var closeCalls = 0;
        var trials = 0;
        foreach (var batch in result.Batches)
        {
            trials += batch.Trials;
            if (batch.MaxHit is not { } hit)
                continue;
            highest = Math.Max(highest ?? 0, hit);
            if (batch.MinHp is { } hp)
                lowest = Math.Min(lowest ?? long.MaxValue, hp);
            closeCalls += batch.CloseCalls ?? 0;
        }
        return highest is { } h
            ? new Risk(true, h, lowest, Estimators.Wilson(closeCalls, trials))
            : new Risk(false, 0, null, null);
    }
}
