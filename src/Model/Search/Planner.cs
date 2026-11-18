using System.Numerics;

namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// Slots of one type, chosen together: any of <see cref="Pool"/> in each, an item at most as often as
/// <see cref="Available"/> has it, and nothing when <see cref="AllowEmpty"/>.
/// </summary>
public sealed record Unit(
    IReadOnlyList<string> Slots,
    IReadOnlyList<string> Pool,
    IReadOnlyDictionary<string, int> Available,
    bool AllowEmpty
)
{
    /// <summary>Every way to fill the unit's slots: the items in pool order, null for an empty slot.</summary>
    public IEnumerable<IReadOnlyList<string?>> Choices()
    {
        var options = Pool.Cast<string?>().ToList();
        if (AllowEmpty)
            options.Add(null);
        return Fill(options, 0, Slots.Count, new Dictionary<string, int>(StringComparer.Ordinal));
    }

    /// <summary>How many ways there are to fill the unit.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public BigInteger Count
    {
        get
        {
            // Counted, not listed: a unit of two ring slots over hundreds of rings has tens of thousands of choices.
            // ways[n] is how many ways the items so far fill n slots, each item at most as often as it is had.
            var slots = Slots.Count;
            var ways = new BigInteger[slots + 1];
            ways[0] = BigInteger.One;
            foreach (var item in Pool)
            {
                var most = Math.Min(slots, Available.GetValueOrDefault(item));
                var next = new BigInteger[slots + 1];
                for (var n = 0; n <= slots; n++)
                for (var take = 0; take <= most && take <= n; take++)
                    next[n] += ways[n - take];
                ways = next;
            }
            // Empty slots make up the rest, however many there are.
            return AllowEmpty ? ways.Aggregate(BigInteger.Zero, (n, w) => n + w) : ways[slots];
        }
    }

    // Combinations with repetition, in pool order, so two slots of one type never give the same pair twice.
    private IEnumerable<IReadOnlyList<string?>> Fill(
        List<string?> options,
        int from,
        int left,
        Dictionary<string, int> used
    )
    {
        if (left == 0)
        {
            yield return [];
            yield break;
        }
        for (var i = from; i < options.Count; i++)
        {
            var item = options[i];
            if (item != null && used.GetValueOrDefault(item) >= Available.GetValueOrDefault(item))
                continue;
            if (item != null)
                used[item] = used.GetValueOrDefault(item) + 1;
            foreach (var rest in Fill(options, i, left - 1, used))
                yield return [item, .. rest];
            if (item != null)
                used[item]--;
        }
    }
}

/// <summary>
/// One weapon in one stance, with what that fixes (the weapon's slot, the slots it takes, locked slots) and the
/// units still to choose. Every set of a branch fights in the same style.
/// </summary>
public sealed record Branch(
    string Style,
    string? Weapon,
    string Stance,
    IReadOnlyDictionary<string, string?> Fixed,
    IReadOnlyList<Unit> Units
)
{
    /// <summary>How many sets the branch holds.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public BigInteger Count => Units.Aggregate(BigInteger.One, (n, unit) => n * unit.Count);
}

/// <summary>
/// What a search will fight. <see cref="Candidates"/> starts with current gear. <see cref="Possible"/> counts the
/// sets the character could wear under the request's locks and exclusions, before any rule; <see cref="RuledOut"/>
/// counts those a rule removed, by rule. With <see cref="Climb"/>, too many are left to list: the search improves
/// a slot at a time over <see cref="Branches"/> instead.
/// </summary>
public sealed record SearchPlan
{
    public const string Beaten = "beaten";
    public const string Totals = "totals";
    public const string SameStats = "same";

    public IReadOnlyList<Candidate> Candidates { get; init; } = [];
    public BigInteger Possible { get; init; }
    public IReadOnlyDictionary<string, BigInteger> RuledOut { get; init; } = new Dictionary<string, BigInteger>();
    public IReadOnlyList<LeftOut> LeftOut { get; init; } = [];
    public bool Climb { get; init; }
    public IReadOnlyList<SkippedStyle> SkippedStyles { get; init; } = [];

    /// <summary>The branches after an item another beats is taken out of each unit.</summary>
    public IReadOnlyList<Branch> Branches { get; init; } = [];

    /// <summary>Whether anything but current gear is left to try.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NothingToSearch => !Climb && Candidates.Count <= 1;
}

/// <summary>
/// Turns a catalogue and a request into the sets to fight. It decides only what the game fixes before a fight
/// (what fits a slot, what a weapon takes, what is owned, locked or excluded) and applies the two comparing rules
/// when the goal's metric allows them. It models no fight.
/// </summary>
public static class Planner
{
    /// <summary>More sets than this are never listed: the search climbs instead.</summary>
    public const int MostCandidates = 20_000;

    /// <summary>
    /// The plan for <paramref name="request"/>. <paramref name="comparing"/> says whether the comparing rules are
    /// used; left out, the goal's metric decides.
    /// </summary>
    public static SearchPlan Plan(GearCatalogue catalogue, SearchRequest request, bool? comparing = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(request);
        var skipped = new List<SkippedStyle>();
        var leftOut = new List<LeftOut>();
        var branches = Branches(catalogue, request, skipped, leftOut);
        var possible = branches.Aggregate(BigInteger.Zero, (n, b) => n + b.Count);
        var ruledOut = new Dictionary<string, BigInteger>();
        // What is equal, and what a stat is worth, depends on the stance a branch fights in.
        var equal = new Dictionary<Branch, Dictionary<string, List<string>>>();
        var measures = new Dictionary<string, Measure>(StringComparer.Ordinal);
        Measure MeasureOf(Branch branch)
        {
            if (!measures.TryGetValue(branch.Stance, out var measure))
                measures[branch.Stance] = measure = new Measure(catalogue, request.Goal, branch.Stance);
            return measure;
        }
        var compare = comparing ?? Goal.AllowsComparing(request.Goal.Metric);
        if (compare)
        {
            branches =
            [
                .. branches.Select(b =>
                {
                    var same = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    var pruned = Dominance.Items(b, MeasureOf(b), same);
                    equal[pruned] = same;
                    return pruned;
                }),
            ];
            var left = branches.Aggregate(BigInteger.Zero, (n, b) => n + b.Count);
            ruledOut[SearchPlan.Beaten] = possible - left;
        }
        var sets = new List<(Branch Branch, IReadOnlyDictionary<string, string?> Worn)>();
        var climb = request.SlotAtATime;
        if (!climb)
        {
            foreach (var branch in branches)
            {
                var found = compare
                    ? Dominance.Totals(branch, MeasureOf(branch), MostCandidates - sets.Count)
                    : Listed(branch, MostCandidates - sets.Count);
                if (found == null)
                {
                    climb = true;
                    break;
                }
                sets.AddRange(found.Select(worn => (branch, worn)));
            }
        }
        var candidates = new List<Candidate> { new(0, ChangeList.Empty, CurrentStyle(catalogue) ?? "") };
        if (!climb)
        {
            if (compare)
            {
                var left = branches.Aggregate(BigInteger.Zero, (n, b) => n + b.Count);
                ruledOut[SearchPlan.Totals] = left - sets.Count;
            }
            foreach (var (branch, worn) in sets)
            {
                var changes = Changes(catalogue, branch, worn);
                if (changes.IsEmpty)
                    continue;
                candidates.Add(
                    new Candidate(candidates.Count, changes, branch.Style)
                    {
                        Same = equal.TryGetValue(branch, out var same) ? Same(catalogue, branch, worn, same) : [],
                        Ignoring = catalogue.Locked(changes),
                    }
                );
            }
        }
        return new SearchPlan
        {
            Candidates = candidates,
            Possible = possible,
            RuledOut = ruledOut,
            LeftOut =
            [
                .. catalogue
                    .Items.Where(i =>
                        i.Owned > 0
                        && !i.Wearable
                        && !request.IncludeUnowned
                        && i.Worn == null
                        && !request.Excluded.Contains(i.Id)
                    )
                    .Select(i => new LeftOut(i.Name, i.Unmet[0])),
                .. catalogue
                    .Items.Where(i => Usable(i, request) && i.Weapon == null && i.Occupies.Count > 0 && i.Worn == null)
                    .Select(i => new LeftOut(i.Name, Note.Of("takes more than one slot"))),
                .. leftOut,
            ],
            Climb = climb,
            SkippedStyles = skipped,
            Branches = branches,
        };
    }

    /// <summary>
    /// The equip and stance changes that turn current gear into <paramref name="worn"/> on
    /// <paramref name="branch"/>; empty when it is current gear. A slot the weapon takes is left to the game, which
    /// empties it when the weapon goes on. A new weapon always names its stance: the game gives it its own first.
    /// </summary>
    public static ChangeList Changes(GearCatalogue catalogue, Branch branch, IReadOnlyDictionary<string, string?> worn)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(worn);
        var taken = branch.Weapon == null ? [] : catalogue.Item(branch.Weapon)?.Occupies ?? [];
        var changes = new List<Change>();
        var newWeapon = false;
        foreach (var slot in catalogue.Slots)
        {
            if (!worn.TryGetValue(slot.Id, out var item) || taken.Contains(slot.Id))
                continue;
            if (item == catalogue.Current.GetValueOrDefault(slot.Id))
                continue;
            changes.Add(new Change(ChangeKind.Equip, slot.Id, item ?? Change.None));
            newWeapon |= item != null && item == branch.Weapon;
        }
        if (branch.Stance.Length > 0 && (newWeapon || branch.Stance != catalogue.Stance))
            changes.Add(new Change(ChangeKind.Stance, null, branch.Stance));
        return ChangeList.Of(changes);
    }

    /// <summary>What a branch's set wears: the fixed slots and one choice per unit.</summary>
    public static Dictionary<string, string?> Worn(Branch branch, IReadOnlyList<IReadOnlyList<string?>> choices)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(choices);
        var worn = new Dictionary<string, string?>(branch.Fixed, StringComparer.Ordinal);
        for (var u = 0; u < branch.Units.Count; u++)
        for (var s = 0; s < branch.Units[u].Slots.Count; s++)
            worn[branch.Units[u].Slots[s]] = choices[u][s];
        return worn;
    }

    private static string? CurrentStyle(GearCatalogue catalogue) =>
        catalogue.Items.FirstOrDefault(i => i.Worn != null && i.Weapon != null)?.Weapon!.Style;

    // One branch per weapon and stance. The weapons are those of the setup weapon's style, or of every style; with
    // the weapon's slot locked, only the one worn.
    private static List<Branch> Branches(
        GearCatalogue catalogue,
        SearchRequest request,
        List<SkippedStyle> skipped,
        List<LeftOut> leftOut
    )
    {
        var current = catalogue.Items.FirstOrDefault(i => i.Worn != null && i.Weapon != null);
        var style = current?.Weapon!.Style;
        var weapons = catalogue
            .Items.Where(i => i.Weapon != null && Usable(i, request))
            .Where(i => request.AllStyles || style == null || i.Weapon!.Style == style)
            .ToList();
        if (current != null && request.Locks.Contains(current.Worn!))
            weapons = [current];
        else if (current == null && WeaponSlots(catalogue).Any(request.Locks.Contains))
            weapons = [];
        var branches = new List<Branch>();
        foreach (var group in weapons.GroupBy(w => w.Weapon!.Style))
        {
            if (catalogue.UnusableStyles.TryGetValue(group.Key, out var why))
            {
                skipped.Add(new SkippedStyle(group.Key, why));
                continue;
            }
            foreach (var weapon in group)
            {
                // A weapon's slots and units are the same in each of its stances.
                var (branch, unusable) = Branch(catalogue, request, weapon);
                if (branch != null)
                    branches.AddRange(
                        Stances(catalogue, request, weapon).Select(stance => branch with { Stance = stance })
                    );
                else if (unusable != null && leftOut.All(l => l.What != weapon.Name))
                    leftOut.Add(new LeftOut(weapon.Name, unusable));
            }
        }
        return branches;
    }

    // A locked weapon fixes its branch: it fights in the stance fought in now, when its type has that stance.
    private static IReadOnlyList<string> Stances(GearCatalogue catalogue, SearchRequest request, GearItem weapon) =>
        weapon.Worn != null && request.Locks.Contains(weapon.Worn) && weapon.Weapon!.Stances.Contains(catalogue.Stance)
            ? [catalogue.Stance]
            : weapon.Weapon!.Stances;

    private static IEnumerable<string> WeaponSlots(GearCatalogue catalogue)
    {
        var types = catalogue
            .Items.Where(i => i.Weapon != null)
            .Select(i => i.SlotType)
            .ToHashSet(StringComparer.Ordinal);
        return catalogue.Slots.Where(s => types.Contains(s.Type)).Select(s => s.Id);
    }

    private static bool Usable(GearItem item, SearchRequest request) =>
        item.Owned > 0 && (item.Wearable || request.IncludeUnowned) && !request.Excluded.Contains(item.Id);

    // The weapon's branch, its stance still to set. No branch when the weapon can't be worn under the locks (it takes a locked slot, or its slot is held), which
    // needs no saying, or when it has nothing to fire, which does.
    private static (Branch? Branch, Note? Why) Branch(GearCatalogue catalogue, SearchRequest request, GearItem weapon)
    {
        var slot = weapon.Worn ?? catalogue.Slots.FirstOrDefault(s => s.Unlocked && s.Type == weapon.SlotType)?.Id;
        if (slot == null)
            return (null, null);
        var isWorn = weapon.Worn != null;
        if (!isWorn && (request.Locks.Contains(slot) || weapon.Occupies.Any(request.Locks.Contains)))
            return (null, null);
        var fixedSlots = new Dictionary<string, string?>(StringComparer.Ordinal) { [slot] = weapon.Id };
        foreach (var taken in weapon.Occupies)
            fixedSlots[taken] = null;
        // A locked slot keeps its item, and that item is then not free for another slot.
        var held = new Dictionary<string, int>(StringComparer.Ordinal) { [weapon.Id] = 1 };
        foreach (var locked in request.Locks)
        {
            if (fixedSlots.ContainsKey(locked) || catalogue.Slots.All(s => s.Id != locked))
                continue;
            var item = catalogue.Current.GetValueOrDefault(locked);
            fixedSlots[locked] = item;
            if (item != null)
                held[item] = held.GetValueOrDefault(item) + 1;
        }
        var units = new List<Unit>();
        var needsAmmo = weapon.Weapon!.AmmoType;
        foreach (
            var type in catalogue.Slots.Where(s => s.Unlocked && !fixedSlots.ContainsKey(s.Id)).GroupBy(s => s.Type)
        )
        {
            var pool = catalogue
                .Items.Where(i => i.SlotType == type.Key && i.Weapon == null && i.Occupies.Count == 0)
                .Where(i => Usable(i, request))
                .ToList();
            // The slot ammo goes in holds only what this weapon fires, and is never left empty.
            var isAmmo = needsAmmo != null && catalogue.Items.Any(i => i.SlotType == type.Key && i.AmmoType != null);
            if (isAmmo)
                pool = [.. pool.Where(i => i.AmmoType == needsAmmo)];
            var available = pool.ToDictionary(
                i => i.Id,
                i => i.Stackable ? int.MaxValue : i.Owned - held.GetValueOrDefault(i.Id),
                StringComparer.Ordinal
            );
            pool = [.. pool.Where(i => available[i.Id] > 0)];
            if (isAmmo && pool.Count == 0)
                return (null, Note.Of("nothing owned for it to fire"));
            units.Add(
                new Unit([.. type.Select(s => s.Id)], [.. pool.Select(i => i.Id)], available, AllowEmpty: !isAmmo)
            );
        }
        return (new Branch(weapon.Weapon!.Style, weapon.Id, "", fixedSlots, units), null);
    }

    // Every set of a branch, or null when there are more than the room left.
    private static List<IReadOnlyDictionary<string, string?>>? Listed(Branch branch, int room)
    {
        if (branch.Count > room)
            return null;
        IEnumerable<List<IReadOnlyList<string?>>> sets =
        [
            [],
        ];
        foreach (var unit in branch.Units)
        {
            var choices = unit.Choices().ToList();
            sets = sets.SelectMany(set => choices.Select(choice => (List<IReadOnlyList<string?>>)[.. set, choice]));
        }
        return [.. sets.Select(set => (IReadOnlyDictionary<string, string?>)Worn(branch, set))];
    }

    // The sets that differ from this one by one item the comparison found equal to the one worn.
    private static List<ChangeList> Same(
        GearCatalogue catalogue,
        Branch branch,
        IReadOnlyDictionary<string, string?> worn,
        Dictionary<string, List<string>> equal
    )
    {
        var same = new List<ChangeList>();
        foreach (var (slot, item) in worn)
        {
            if (item == null || !equal.TryGetValue(item, out var others))
                continue;
            foreach (var other in others)
            {
                var swapped = new Dictionary<string, string?>(worn, StringComparer.Ordinal) { [slot] = other };
                var changes = Changes(catalogue, branch, swapped);
                if (!changes.IsEmpty && !same.Contains(changes))
                    same.Add(changes);
            }
        }
        return same;
    }
}
