namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// The sets offered beside a setup search's best: from the sets it fought as long as the best, those whose gain over
/// the setup as it is lies in the best's 95% range. Cheaper uses fewer supplies than the best (of several, the least
/// of the supply the best uses most of); Safer takes the least damage, less than the best's.
/// </summary>
public static class AlternativePicks
{
    /// <summary>A set fought as long as the best: its place, gain, supplies and damage taken.</summary>
    public sealed record Fought(int Index, Estimate? Gain, IReadOnlyList<SupplyCost> Supplies, Estimate? DamageTaken);

    /// <summary>The places of Cheaper and Safer among <paramref name="sets"/>; null where none qualifies.</summary>
    public static (int? Cheaper, int? Safer) Pick(Fought best, IReadOnlyList<Fought> sets)
    {
        ArgumentNullException.ThrowIfNull(best);
        ArgumentNullException.ThrowIfNull(sets);
        if (best.Gain is not { } range)
            return (null, null);
        var within = sets.Where(s =>
                s.Index != best.Index && s.Gain is { } gain && gain.Point >= range.Low && gain.Point <= range.High
            )
            .ToList();
        var most = best.Supplies.Count > 0 ? best.Supplies[0].Key : null;
        var cheaper = within
            .Where(s => SupplyCosts.FewerSupplies(s.Supplies, best.Supplies))
            .OrderBy(s => most == null ? 0 : SupplyCosts.Point(s.Supplies, most))
            .ThenByDescending(s => s.Gain!.Point)
            .ThenBy(s => s.Index)
            .FirstOrDefault();
        var safer = best.DamageTaken is not { } taken
            ? null
            : within
                .Where(s => s.DamageTaken is { } d && d.Point < taken.Point)
                .OrderBy(s => s.DamageTaken!.Point)
                .ThenByDescending(s => s.Gain!.Point)
                .ThenBy(s => s.Index)
                .FirstOrDefault();
        return (cheaper?.Index, safer?.Index);
    }
}
