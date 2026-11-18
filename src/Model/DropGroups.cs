namespace MelvorMods.CombatSimulator2.Model;

/// <summary>Where a drop stands when two setups are compared.</summary>
public enum DropGroup
{
    /// <summary>A rare item the game's chances cover: shown as the time one takes.</summary>
    Rare,

    /// <summary>A clear difference per kill: it changes for a reason of its own, not the kill rate.</summary>
    NotWithKills,

    /// <summary>No clear difference per kill: it rises and falls with the kill rate alone.</summary>
    WithKills,
}

/// <summary>
/// Sorts a comparison's drops so the kill rate's change is said once: most drops only follow it, and the few that
/// don't are the ones worth reading.
/// </summary>
public static class DropGroups
{
    /// <summary>
    /// The group of <paramref name="item"/>: rare when either side's chances give a time for one, else by
    /// <paramref name="perKill"/>, the difference in the item per kill.
    /// </summary>
    public static DropGroup Of(TargetResult? a, TargetResult? b, string item, Difference? perKill)
    {
        if (
            (a is { HasEstimates: true } && ForOnes.Time(a, item) != null)
            || (b is { HasEstimates: true } && ForOnes.Time(b, item) != null)
        )
            return DropGroup.Rare;
        return perKill?.Verdict is Verdict.Better or Verdict.Worse ? DropGroup.NotWithKills : DropGroup.WithKills;
    }
}
