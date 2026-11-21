namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>One item of a gap row: its id, its name and its mark.</summary>
public sealed record GapItem(string Id, string Name, Mark? Mark);

/// <summary>
/// One new item of an answer, with the company it needs, swapped alone into current gear: its gain over current gear
/// in percent with its 95% range, that gain as a share of the answer's, and whether its range reaches into the margin,
/// so it pays off only with the rest of the set.
/// </summary>
public sealed record GapRow(IReadOnlyList<GapItem> Items, Estimate Gain, double Share, bool OnlyWithSet);

/// <summary>A swap the gap stage fights: the new items it is about, and the changes it puts on current gear.</summary>
public sealed record GapSwap(IReadOnlyList<string> Items, ChangeList Changes);

/// <summary>
/// The gap from now to an answer: which of its new items (not owned, or not wearable yet) to get first. Each is swapped
/// alone into current gear with the company it needs from the answer: a weapon with the answer's ammo, offhand and
/// stance; a set bonus's piece with every piece of that set the answer holds. No sum is made of the rows: items
/// that pay off together would be counted twice or not at all.
/// </summary>
public static class GapRows
{
    /// <summary>The swaps for <paramref name="set"/>'s new items, in the game's slot order.</summary>
    public static IReadOnlyList<GapSwap> Swaps(GearCatalogue catalogue, ChangeList set)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(set);
        var equips = set.Where(c => c.Kind == ChangeKind.Equip && c.Value != Change.None).ToList();
        bool IsNew(Change change) => catalogue.Item(change.Value) is { } item && (item.Owned == 0 || !item.Wearable);
        var swaps = new List<GapSwap>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in catalogue.Slots)
        {
            if (
                equips.Find(c => c.Subject == slot.Id) is not { } change
                || !IsNew(change)
                || taken.Contains(change.Value)
            )
                continue;
            var item = catalogue.Item(change.Value)!;
            var company = new List<Change> { change };
            if (item.Weapon != null)
            {
                // The weapon fights with the answer's ammo, offhand and stance, whatever they are.
                company.AddRange(equips.Where(c => c != change && IsAmmoOrOffhand(catalogue, c)));
                company.AddRange(set.Where(c => c.Kind == ChangeKind.Stance));
            }
            if (item.Sets.Count > 0)
                company.AddRange(
                    equips.Where(c => c != change && catalogue.Item(c.Value)?.Sets.Intersect(item.Sets).Any() == true)
                );
            var changes = ChangeList.Of(company.Distinct());
            foreach (var c in changes.Where(c => c.Kind == ChangeKind.Equip))
                taken.Add(c.Value);
            // The item the row is about first, then the new items in its company.
            swaps.Add(
                new GapSwap(
                    [.. company.Distinct().Where(c => c.Kind == ChangeKind.Equip && IsNew(c)).Select(c => c.Value)],
                    changes
                )
            );
        }
        return swaps;
    }

    /// <summary>
    /// The rows from the swaps' results against current gear's, largest gain first; a swap without a result is left
    /// out. <paramref name="gap"/> is the answer's gain in percent; <paramref name="margin"/> the share within which a
    /// set is as good.
    /// </summary>
    public static List<GapRow> Rows(
        GearCatalogue catalogue,
        IReadOnlyList<GapSwap> swaps,
        StageResult current,
        IReadOnlyDictionary<int, StageResult> results,
        Estimate gap,
        double margin,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(swaps);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(gap);
        var rows = new List<GapRow>();
        for (var i = 0; i < swaps.Count; i++)
        {
            if (
                !results.TryGetValue(i, out var fought)
                || Race.Difference(current, fought, prices).Percent is not { } gain
            )
                continue;
            var items = swaps[i]
                .Items.Select(id =>
                    catalogue.Item(id) is { } item
                        ? new GapItem(id, item.Name, Mark.OfItem(item))
                        : new GapItem(id, Ids.Brief(id), null)
                )
                .ToList();
            var share = gap.Point != 0 ? gain.Point / gap.Point : 0;
            // As "Find upgrades" counts a gain: only when its whole range clears the margin.
            rows.Add(new GapRow(items, gain, share, gain.Low <= margin));
        }
        return [.. rows.OrderByDescending(r => r.Gain.Point)];
    }

    private static bool IsAmmoOrOffhand(GearCatalogue catalogue, Change change) =>
        catalogue.Item(change.Value) is { } item
        && (
            item.AmmoType != null
            || catalogue.Slots.Any(s => s.Id == change.Subject && s.Type == OffhandType(catalogue))
        );

    // The slot type a two-handed weapon takes beside its own: what the catalogue's weapons occupy.
    private static string? OffhandType(GearCatalogue catalogue) =>
        catalogue
            .Items.SelectMany(i => i.Occupies)
            .Select(slot => catalogue.Slots.FirstOrDefault(s => s.Id == slot)?.Type)
            .FirstOrDefault(t => t != null && catalogue.Items.All(i => i.AmmoType == null || i.SlotType != t));
}
