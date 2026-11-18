using System.Security.Cryptography;
using System.Text;

namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>Which options of a choice a search tries, and the prayer pairs it builds from the best singles.</summary>
public static class Choices
{
    /// <summary>Pairs are made from this many of the best single prayers: six pairs at most.</summary>
    public const int PairsFrom = 4;

    /// <summary>
    /// The options of <paramref name="choice"/> to race against the setup's own: candidates the held weapon's
    /// <paramref name="elements"/> and stance allow, never the setup's own option again.
    /// </summary>
    public static IReadOnlyList<ChoiceOption> For(
        Choice choice,
        IReadOnlyCollection<string> elements,
        bool magicStance,
        bool includeUnowned
    )
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(elements);
        return
        [
            .. choice.Options.Where(o =>
                o.IsCandidate(includeUnowned) && o.Allowed(elements, magicStance) && !Same(o, choice.Current)
            ),
        ];
    }

    /// <summary>
    /// Every pair of the first <see cref="PairsFrom"/> single prayers in <paramref name="best"/> (best first) when
    /// two can be on at once, without the setup's own pair; a prayer is never paired with itself.
    /// </summary>
    public static IReadOnlyList<ChoiceOption> Pairs(Choice prayers, IReadOnlyList<ChoiceOption> best)
    {
        ArgumentNullException.ThrowIfNull(prayers);
        ArgumentNullException.ThrowIfNull(best);
        if (prayers.Kind != ChoiceKind.Prayers || prayers.Slots < 2)
            return [];
        var singles = best.Where(o => o.Ids.Count == 1).DistinctBy(o => o.Ids[0]).Take(PairsFrom).ToList();
        var pairs = new List<ChoiceOption>();
        for (var i = 0; i < singles.Count; i++)
        for (var j = i + 1; j < singles.Count; j++)
        {
            var (a, b) = (singles[i], singles[j]);
            var pair = new ChoiceOption(
                ChoiceKind.Prayers,
                [a.Ids[0], b.Ids[0]],
                PrayerChanges(prayers.Current.Ids, [a.Ids[0], b.Ids[0]])
            )
            {
                Unmet = [.. a.Unmet, .. b.Unmet],
                Opaque = a.Opaque || b.Opaque,
                Names = [.. a.Names, .. b.Names],
            };
            if (!Same(pair, prayers.Current))
                pairs.Add(pair);
        }
        return pairs;
    }

    /// <summary>
    /// What <paramref name="choice"/> adds to a pass with the setup's own weapon: its options one at a time, and the
    /// most pairs its singles can make.
    /// </summary>
    public static KindCount Count(
        Choice choice,
        IReadOnlyCollection<string> elements,
        bool magicStance,
        bool includeUnowned
    )
    {
        ArgumentNullException.ThrowIfNull(choice);
        var options = For(choice, elements, magicStance, includeUnowned)
            .Count(o => choice.Kind != ChoiceKind.Prayers || o.Ids.Count <= 1);
        var singles = Math.Min(
            PairsFrom,
            For(choice, elements, magicStance, includeUnowned).Count(o => o.Ids.Count == 1)
        );
        var pairs = choice.Kind == ChoiceKind.Prayers && choice.Slots >= 2 ? singles * (singles - 1) / 2 : 0;
        return new KindCount(choice.Kind, choice.Name, options, pairs, choice.Slots);
    }

    /// <summary>
    /// The changes that take a setup from the prayers <paramref name="from"/> to <paramref name="to"/>: those not
    /// kept turned off, the new ones on.
    /// </summary>
    public static ChangeList PrayerChanges(IReadOnlyCollection<string> from, IReadOnlyCollection<string> to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return ChangeList.Of(
            from.Except(to, StringComparer.Ordinal)
                .Select(p => new Change(ChangeKind.Prayer, p, Change.Off))
                .Concat(
                    to.Except(from, StringComparer.Ordinal).Select(p => new Change(ChangeKind.Prayer, p, Change.On))
                )
        );
    }

    /// <summary>Whether two options put on the same: the same ids, in any order.</summary>
    public static bool Same(ChoiceOption a, ChoiceOption b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Kind == b.Kind
            && a.Ids.Order(StringComparer.Ordinal).SequenceEqual(b.Ids.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// What the choices offer the character, as one short hash: the options each kind can use now and the setup's
    /// own. It changes when food or toolbelt items run out or come in, or a prayer, spell or imbuement unlocks.
    /// </summary>
    public static string SuppliesHash(IEnumerable<Choice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var lines = choices
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Subject, StringComparer.Ordinal)
            .SelectMany(c =>
                c.Options.Where(o => o.Usable)
                    .Select(o => $"{c.Name}={string.Join('+', o.Ids)}")
                    .Append($"{c.Name}:{string.Join('+', c.Current.Ids)}")
            );
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))))[..12];
    }

    /// <summary>
    /// The spell a magic weapon of <paramref name="elements"/> fights with until the spell step picks one: the
    /// strongest the search may try (the game lists them weakest first); null when it allows none.
    /// </summary>
    public static ChoiceOption? SpellFor(Choice spells, IReadOnlyCollection<string> elements, bool includeUnowned)
    {
        ArgumentNullException.ThrowIfNull(spells);
        return spells.Options.LastOrDefault(o =>
            !o.IsNone && o.IsCandidate(includeUnowned) && o.Allowed(elements, magicStance: true)
        );
    }

    /// <summary>
    /// <paramref name="candidates"/> with a spell for each magic set that holds none it can cast, so it can fight:
    /// the strongest its weapon allows (current gear's weapon for a set that keeps it).
    /// </summary>
    public static IReadOnlyList<Candidate> WithSpells(
        IReadOnlyList<Candidate> candidates,
        GearCatalogue catalogue,
        Choice spells,
        bool includeUnowned
    )
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(spells);
        var worn = catalogue.Items.FirstOrDefault(i => i.Worn != null && i.Weapon != null)?.Id;
        return
        [
            .. candidates.Select(c =>
            {
                if (c.Style != Magic || c.Changes.Any(change => change.Kind == ChangeKind.Spell))
                    return c;
                var weapon =
                    c.Changes.Select(change => change.Item)
                        .FirstOrDefault(item => item != null && catalogue.Item(item)?.Weapon != null)
                    ?? worn;
                var elements = weapon is { } id ? catalogue.Item(id)?.Weapon?.Elements ?? [] : [];
                return SpellFor(spells, elements, includeUnowned) is { } spell
                    ? c with
                    {
                        Changes = c.Changes.With(spell.Changes[0]),
                    }
                    : c;
            }),
        ];
    }

    /// <summary>The combat style whose sets cast spells.</summary>
    public const string Magic = "Magic";
}
