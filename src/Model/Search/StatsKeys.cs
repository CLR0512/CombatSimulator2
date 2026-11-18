using System.Globalization;

namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// Sets the game gives the same stats against the target are fought once. A set's key is its fight numbers, read
/// from the game with the target in front of it, and beside them what its items carry that those numbers can't stand
/// for: modifiers, effects, special attacks, set pieces, what is used up in fights, and any equipment stat no stance
/// reads. Two sets merge only when both halves are equal, so a bonus to XP or drops keeps a set apart however it
/// fights.
/// </summary>
public static class StatsKeys
{
    /// <summary>
    /// What <paramref name="changes"/> leaves worn that its fight numbers can't stand for, as text: equal text means
    /// equal parts, whichever items carry them.
    /// </summary>
    public static string Rest(GearCatalogue catalogue, ChangeList changes)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(changes);
        var worn = new Dictionary<string, string>(catalogue.Current, StringComparer.Ordinal);
        foreach (var change in changes.Where(c => c.Kind == ChangeKind.Equip && c.Subject != null))
        {
            if (change.Value == Change.None)
                worn.Remove(change.Subject!);
            else
                worn[change.Subject!] = change.Value;
        }
        // What the weapon takes beside its own slot, the game empties.
        foreach (var item in worn.Values.ToList())
        foreach (var taken in catalogue.Item(item)?.Occupies ?? [])
            worn.Remove(taken);
        var read = catalogue
            .StanceReads.Values.SelectMany(s => s)
            .Concat(catalogue.EvasionStats)
            .ToHashSet(StringComparer.Ordinal);
        // The game adds equipment stats and modifiers into totals, so a set is its totals: two items that add up
        // to a third are the same as it.
        var totals = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var parts = new List<string>();
        foreach (var id in worn.Values)
        {
            if (catalogue.Item(id) is not { } item || item.Opaque)
            {
                parts.Add("opaque " + id);
                continue;
            }
            foreach (var (stat, value) in item.Stats.Where(s => !read.Contains(s.Key)))
                totals["stat " + stat] = totals.GetValueOrDefault("stat " + stat) + value;
            foreach (var bonus in item.Bonuses)
            {
                var name = $"mod {bonus.Stat}@{bonus.Context}/{bonus.InForce}";
                totals[name] = totals.GetValueOrDefault(name) + bonus.Value;
            }
            parts.AddRange(item.Attacks.Select(a => "attack " + a));
            parts.AddRange(item.Effects.Select(e => $"effect {e.Kind}@{e.Trigger}"));
            parts.AddRange(item.Sets.Select(set => "set " + set));
            parts.AddRange(item.ConsumedOn.Select(trigger => $"used {trigger} by {id}"));
        }
        return string.Join(
            "\n",
            totals
                .Where(t => t.Value != 0)
                .Select(t => t.Key + "=" + t.Value.ToString("R", CultureInfo.InvariantCulture))
                .Concat(parts.Order(StringComparer.Ordinal))
        );
    }

    /// <summary>
    /// <paramref name="candidates"/> with those of equal keys merged into the first of them (current gear first),
    /// which names the others in <see cref="Candidate.Same"/>; numbered again from 0. A candidate without a key
    /// is never merged.
    /// </summary>
    public static IReadOnlyList<Candidate> Merge(IReadOnlyList<Candidate> candidates, out int merged)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var kept = new List<Candidate>();
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        merged = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.StatsKey is { } key && byKey.TryGetValue(key, out var at))
            {
                var first = kept[at];
                kept[at] = first with { Same = [.. first.Same, candidate.Changes, .. candidate.Same] };
                merged++;
                continue;
            }
            if (candidate.StatsKey is { } fresh)
                byKey[fresh] = kept.Count;
            kept.Add(candidate with { Index = kept.Count });
        }
        return kept;
    }
}
