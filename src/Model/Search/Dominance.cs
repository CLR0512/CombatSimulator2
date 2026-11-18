namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// Items as numbers that can be compared for one goal: each stat and modifier the goal counts is one dimension,
/// turned so that higher is better by the game's own direction. What can't be put on that scale (effects, attacks,
/// what uses an item up, the sets it counts for) is the item's <see cref="Kind"/>: only items of one kind compare.
/// </summary>
public sealed class Measure
{
    private readonly Dictionary<string, int> _dimensions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double[]> _vectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GearItem> _items;
    private readonly HashSet<string> _sets;
    private readonly double[] _nothing;
    private readonly Dictionary<string, string> _kinds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _comparable = new(StringComparer.Ordinal);

    // Units already ruled on, by their slots and what they hold: branches of one stance share most of their units,
    // and a full game's catalogue has over a thousand branches.
    internal Dictionary<string, (Unit Unit, List<(string Twin, string Other)> Equal)> Ruled { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The scale for sets fighting in <paramref name="stance"/> (any stance when null): an equipment stat the
    /// stance and the target never read is no dimension of it.
    /// </summary>
    public Measure(GearCatalogue catalogue, Goal goal, string? stance = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(goal);
        _items = catalogue.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        // A set whose every bonus acts on something the goal doesn't count is no reason to wear its pieces.
        _sets = catalogue
            .SetBonuses.Where(set =>
                set.Steps.Values.SelectMany(step => step)
                    .Any(bonus => goal.Counts(bonus.Group, catalogue.TargetAttacks))
            )
            .Select(set => set.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in catalogue.Items)
        {
            foreach (var stat in item.Stats.Keys)
            {
                var group = catalogue.StatGroups.GetValueOrDefault(stat, PartGroup.Other);
                if (goal.Counts(group, catalogue.TargetAttacks) && catalogue.Reads(stat, stance))
                    _dimensions.TryAdd("stat:" + stat, _dimensions.Count);
            }
            foreach (var bonus in item.Bonuses)
                if (goal.Counts(bonus.Group, catalogue.TargetAttacks))
                    _dimensions.TryAdd(Key(bonus), _dimensions.Count);
        }
        _nothing = new double[_dimensions.Count];
        foreach (var item in catalogue.Items)
        {
            var vector = new double[_dimensions.Count];
            foreach (var (stat, value) in item.Stats)
                if (_dimensions.TryGetValue("stat:" + stat, out var at))
                    vector[at] += value;
            foreach (var bonus in item.Bonuses)
                if (_dimensions.TryGetValue(Key(bonus), out var at))
                    vector[at] += bonus.Direction == Direction.Bad ? -bonus.Value : bonus.Value;
            _vectors[item.Id] = vector;
        }
    }

    public int Dimensions => _dimensions.Count;

    public bool IsWorn(string? item) => item != null && _items[item].Worn != null;

    /// <summary>The item's place on the scale; all zero for an empty slot.</summary>
    public double[] Vector(string? item) => item == null ? _nothing : _vectors[item];

    /// <summary>Whether the item may be compared at all: never when opaque or with a modifier the game calls neither
    /// good nor bad.</summary>
    public bool Comparable(string? item)
    {
        if (item == null)
            return true;
        if (!_comparable.TryGetValue(item, out var comparable))
            _comparable[item] = comparable =
                _items[item] is { Opaque: false } i && i.Bonuses.All(b => b.Direction != Direction.Neutral);
        return comparable;
    }

    /// <summary>What the scale doesn't hold; "" for an empty slot and for a plain item of no set.</summary>
    public string Kind(string? item)
    {
        if (item == null)
            return "";
        if (_kinds.TryGetValue(item, out var kind))
            return kind;
        var i = _items[item];
        var parts = i
            .Effects.Select(e => $"effect:{e.Kind}/{e.Trigger}")
            .Concat(i.Attacks.Select(a => "attack:" + a))
            .Concat(i.ConsumedOn.Select(t => "consumed:" + t))
            .Concat(i.Sets.Where(_sets.Contains).Select(s => "set:" + s))
            .Order(StringComparer.Ordinal);
        return _kinds[item] = string.Join(' ', parts.Append(i.AmmoType == null ? "" : "ammo:" + i.AmmoType)).Trim();
    }

    /// <summary>
    /// What a set must share with another for their totals to compare: each set bonus as often as its pieces are
    /// worn, and every item of a kind of its own by id. "" for a plain item.
    /// </summary>
    public string Token(string? item)
    {
        if (item == null)
            return "";
        var i = _items[item];
        if (!Comparable(item) || i.Special || i.ConsumedOn.Count > 0)
            return "item:" + item;
        return string.Join(' ', i.Sets.Where(_sets.Contains).Order(StringComparer.Ordinal).Select(s => "set:" + s));
    }

    /// <summary>Whether <paramref name="a"/> is at least as good as <paramref name="b"/> in everything counted.</summary>
    public bool Beats(string? a, string? b)
    {
        if (!Comparable(a) || !Comparable(b) || Kind(a) != Kind(b))
            return false;
        return AtLeast(Vector(a), Vector(b));
    }

    public static bool AtLeast(double[] a, double[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        for (var d = 0; d < a.Length; d++)
            if (a[d] < b[d])
                return false;
        return true;
    }

    private static string Key(PartModifier bonus) => $"modifier:{bonus.Stat}|{bonus.Context}";
}

/// <summary>
/// The two rules that rule sets out by comparing, never by modelling a fight: an item another of its slot beats,
/// and totals no other set of the branch beats. Both rest on one assumption, cleared per metric: more of a counted
/// stat, in the game's own direction, never gives a worse result.
/// </summary>
public static class Dominance
{
    // Partial sets held while totals are built; past it the branch is left to the climb.
    private const int MostHeld = 20_000;

    // Comparisons of partial sets one branch may make, about a quarter of a second: a branch of many items that
    // compare as neither better nor worse would take seconds to find it holds too many sets to list.
    private const long MostCompared = 50_000_000;

    /// <summary>
    /// The branch with each unit's beaten items taken out. An item goes when items that beat it could fill every
    /// slot of the unit without it; of equal items the worn one stays, else the first, and the others are recorded
    /// in <paramref name="equal"/> under it.
    /// </summary>
    public static Branch Items(Branch branch, Measure measure, Dictionary<string, List<string>> equal)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(equal);
        return branch with { Units = [.. branch.Units.Select(unit => Items(unit, measure, equal))] };
    }

    private static Unit Items(Unit unit, Measure measure, Dictionary<string, List<string>> equal)
    {
        var key =
            string.Join(',', unit.Slots)
            + "|"
            + unit.AllowEmpty
            + "|"
            + string.Join(',', unit.Pool.Select(i => i + "=" + unit.Available.GetValueOrDefault(i)));
        if (!measure.Ruled.TryGetValue(key, out var ruled))
            measure.Ruled[key] = ruled = Rule(unit, measure);
        foreach (var (twin, other) in ruled.Equal)
        {
            if (!equal.TryGetValue(twin, out var others))
                equal[twin] = others = [];
            if (!others.Contains(other))
                others.Add(other);
        }
        return ruled.Unit;
    }

    private static (Unit Unit, List<(string Twin, string Other)> Equal) Rule(Unit unit, Measure measure)
    {
        var pairs = new List<(string Twin, string Other)>();
        var entries = unit.Pool.Cast<string?>().ToList();
        if (unit.AllowEmpty)
            entries.Add(null);
        var gone = new HashSet<int>();
        // Of two equal entries the worn one stays, so the set changes less; else the earlier.
        bool First(int a, int b) =>
            measure.IsWorn(entries[a]) != measure.IsWorn(entries[b]) ? measure.IsWorn(entries[a]) : a < b;
        for (var b = 0; b < entries.Count; b++)
        {
            long ahead = 0;
            string? twin = null;
            for (var a = 0; a < entries.Count && ahead < unit.Slots.Count; a++)
            {
                if (a == b || !measure.Beats(entries[a], entries[b]))
                    continue;
                var same = measure.Beats(entries[b], entries[a]);
                if (same && !First(a, b))
                    continue;
                ahead += entries[a] is { } item ? unit.Available.GetValueOrDefault(item) : unit.Slots.Count;
                if (same && entries[a] != null)
                    twin ??= entries[a];
            }
            if (ahead < unit.Slots.Count)
                continue;
            gone.Add(b);
            if (twin != null && entries[b] is { } other)
                pairs.Add((twin, other));
        }
        return (
            unit with
            {
                Pool = [.. unit.Pool.Where((_, i) => !gone.Contains(i))],
                AllowEmpty = unit.AllowEmpty && !gone.Contains(entries.Count - 1),
            },
            pairs
        );
    }

    /// <summary>
    /// The sets of a branch whose totals no other set of the branch beats, built a unit at a time: of two partial
    /// sets with the same sets and special items, the one behind in everything counted can't end ahead. Null when
    /// more than <paramref name="room"/> are left, or too many have to be held on the way.
    /// </summary>
    public static List<IReadOnlyDictionary<string, string?>>? Totals(Branch branch, Measure measure, int room)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(measure);
        var held = new List<Partial> { new([], new double[measure.Dimensions], "") };
        var compared = 0L;
        foreach (var unit in branch.Units)
        {
            var choices = unit.Choices().ToList();
            if ((long)held.Count * choices.Count > MostHeld * 10L)
                return null;
            var next = new List<Partial>(held.Count * choices.Count);
            foreach (var partial in held)
            foreach (var choice in choices)
            {
                var total = (double[])partial.Total.Clone();
                var tokens = new List<string>();
                foreach (var item in choice)
                {
                    var vector = measure.Vector(item);
                    for (var d = 0; d < total.Length; d++)
                        total[d] += vector[d];
                    if (measure.Token(item) is { Length: > 0 } token)
                        tokens.Add(token);
                }
                var with = tokens.Count == 0 ? partial.With : Join(partial.With, tokens);
                next.Add(new Partial([.. partial.Choices, choice], total, with));
            }
            held = [];
            foreach (var group in next.GroupBy(p => p.With, StringComparer.Ordinal))
                // Past the most it may hold, or compare, the branch is the climb's.
                if (!Front(group, held, ref compared))
                    return null;
        }
        if (held.Count > room)
            return null;
        return [.. held.Select(p => (IReadOnlyDictionary<string, string?>)Planner.Worn(branch, p.Choices))];
    }

    private sealed record Partial(IReadOnlyList<IReadOnlyList<string?>> Choices, double[] Total, string With);

    private static string Join(string with, List<string> tokens) =>
        string.Join(
            ' ',
            with.Split(' ', StringSplitOptions.RemoveEmptyEntries).Concat(tokens).Order(StringComparer.Ordinal)
        );

    // Adds to held the partial sets no other of the group beats; of equal totals the first stays. False once held
    // grows past the most it may, or the comparisons made past theirs.
    private static bool Front(IEnumerable<Partial> group, List<Partial> held, ref long compared)
    {
        var front = new List<Partial>();
        foreach (var partial in group.OrderByDescending(p => p.Total.Sum()))
        {
            compared += front.Count;
            if (compared > MostCompared)
                return false;
            if (front.Any(kept => Measure.AtLeast(kept.Total, partial.Total)))
                continue;
            front.Add(partial);
            if (held.Count + front.Count > MostHeld)
                return false;
        }
        held.AddRange(front);
        return true;
    }
}
