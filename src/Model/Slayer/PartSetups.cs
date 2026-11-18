namespace MelvorMods.CombatSimulator2.Model.Slayer;

/// <summary>
/// Which of a side's master monsters are fought with a saved loadout of their own, from its
/// <see cref="ChangeKind.Loadouts"/> changes. Such a monster wears the loadout as saved in place of the side's gear,
/// toolbelt, stance, prayers, spell and food; the side's other changes (levels, purchases, blocks, the slayer task
/// choice) stay. A monster not named is fought with the side's setup.
/// </summary>
public static class PartSetups
{
    /// <summary>The loadout slot of each of <paramref name="partKeys"/> that has one, by its part key.</summary>
    public static Dictionary<string, string> Of(Setup side, IEnumerable<string> partKeys)
    {
        ArgumentNullException.ThrowIfNull(side);
        return Of(side.Changes, partKeys);
    }

    /// <summary>As the other, from a setup's changes.</summary>
    public static Dictionary<string, string> Of(ChangeList? changes, IEnumerable<string> partKeys)
    {
        ArgumentNullException.ThrowIfNull(partKeys);
        var slots = new Dictionary<(string Master, string Enemy), string>();
        foreach (var change in changes?.Where(c => c.Kind == ChangeKind.Loadouts) ?? [])
        {
            foreach (var (enemy, slot) in change.Loadouts)
                slots[(change.Subject!, enemy)] = slot;
        }
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (slots.Count == 0)
            return map;
        foreach (var key in partKeys.Where(Ids.IsPart))
        {
            if (slots.TryGetValue((Ids.MasterOf(key), key[(key.LastIndexOf('/') + 1)..]), out var slot))
                map[key] = slot;
        }
        return map;
    }
}
