namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// One part the search has no rule for: how many items carry it, how many of those are owned, and how many it alone
/// keeps from being compared (the items a rule for it would free), with a few of their names; and the set bonuses
/// that carry it.
/// </summary>
public sealed record CensusRow(
    string Part,
    int Items,
    int Owned,
    int Alone,
    IReadOnlyList<string> Names,
    int SetBonuses
);

/// <summary>
/// What the search can't read across every wearable item in the game, owned or not, so that after a game update one
/// look says which new parts cost the most: the rows are ordered by the items a rule would free, then by the items
/// carrying the part.
/// </summary>
public static class Census
{
    /// <summary>How many names each row keeps.</summary>
    public const int Shown = 5;

    public static IReadOnlyList<CensusRow> Of(GearCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var parts = catalogue
            .Items.SelectMany(i => i.Unknowns)
            .Concat(catalogue.SetBonuses.SelectMany(s => s.Unknowns))
            .Distinct(StringComparer.Ordinal);
        return
        [
            .. parts
                .Select(part =>
                {
                    var carrying = catalogue.Items.Where(i => i.Unknowns.Contains(part)).ToList();
                    var alone = carrying.Where(i => i.Unknowns.Count == 1).ToList();
                    return new CensusRow(
                        part,
                        carrying.Count,
                        carrying.Count(i => i.Owned > 0),
                        alone.Count,
                        [.. (alone.Count > 0 ? alone : carrying).Take(Shown).Select(i => i.Name)],
                        catalogue.SetBonuses.Count(s => s.Unknowns.Contains(part))
                    );
                })
                .OrderByDescending(r => r.Alone)
                .ThenByDescending(r => r.Items)
                .ThenByDescending(r => r.SetBonuses)
                .ThenBy(r => r.Part, StringComparer.Ordinal),
        ];
    }

    /// <summary>The items the search can't compare, of all wearable ones: the census's first line.</summary>
    public static (int Opaque, int Wearable) Totals(GearCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        return (catalogue.Items.Count(i => i.Unknowns.Count > 0 || i.Opaque), catalogue.Items.Count);
    }
}
