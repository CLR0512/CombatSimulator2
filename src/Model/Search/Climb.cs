namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// Improves a set a slot at a time when there are too many sets to fight them all: from current gear, the weapon
/// and stance first, then each slot with the rest held, then each set bonus's pieces worn together, every choice of
/// them for each count the bonus gives something at, then, in a setup search, each kind of choice beyond gear; the
/// winner of each step is kept. Passes repeat until one
/// changes nothing, three at most; a step whose held set is the one it last raced from is passed over, as its race
/// would only repeat itself. It finds a set no
/// single change improves, which may not be the best combination: <see cref="Restart"/> climbs again from the
/// first step's runner-up, to reach a set another weapon leads to.
/// </summary>
public sealed partial class Climb
{
    public const int MostPasses = 3;

    private readonly GearCatalogue _catalogue;
    private readonly IReadOnlyList<Branch> _branches;

    // Each set of the step with its branch (none for current gear held without a weapon) and the choices it holds.
    private readonly Dictionary<
        ChangeList,
        (Branch? Branch, Dictionary<string, string?> Worn, Dictionary<string, ChoiceOption> Chosen)
    > _offered = [];
    private Branch? _branch;
    private Dictionary<string, string?> _worn;
    private int _step;
    private bool _changed;
    private (Branch Branch, Dictionary<string, string?> Worn)? _runnerUp;

    // The set each step last raced from: a held set fixes the weapon, so a step's place means the same slot.
    private readonly Dictionary<int, ChangeList> _ranFrom = [];

    /// <summary>
    /// A climb over gear from current gear, and over the choices of <paramref name="kinds"/> (none: gear alone)
    /// after the gear's steps in each pass.
    /// </summary>
    public Climb(GearCatalogue catalogue, IReadOnlyList<Branch> branches, ChoiceSteps? kinds = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(branches);
        _catalogue = catalogue;
        _branches = branches;
        _kinds = kinds;
        _chosen = Unchosen(kinds);
        _worn = catalogue.Slots.ToDictionary(s => s.Id, s => catalogue.Current.GetValueOrDefault(s.Id));
        var weapon = catalogue.Items.FirstOrDefault(i => i.Worn != null && i.Weapon != null)?.Id;
        _branch = branches.FirstOrDefault(b => b.Weapon == weapon && b.Stance == catalogue.Stance);
        Passes = 1;
    }

    // A climb from another's runner-up: it keeps that weapon, so it starts at the units and never races weapons.
    private Climb(Climb from, Branch branch, Dictionary<string, string?> worn)
    {
        _catalogue = from._catalogue;
        _branches = from._branches;
        _kinds = from._kinds;
        _chosen = Unchosen(_kinds);
        _branch = branch;
        _worn = worn;
        Held = Planner.Changes(_catalogue, branch, worn);
        _step = 1;
        Passes = 1;
        Restarted = true;
    }

    /// <summary>Whether this climb started from another's runner-up.</summary>
    public bool Restarted { get; }

    /// <summary>Whether the step under way is the first climb's weapon step, whose runner-up it keeps.</summary>
    public bool WeaponStep => _step == 0 && Passes == 1 && !Restarted;

    /// <summary>
    /// A climb from the runner-up of this one's weapon step, once this one is done; null for a climb that was
    /// itself restarted, or one whose weapon step had no runner-up.
    /// </summary>
    public Climb? Restart() =>
        Done && !Restarted && _runnerUp is { } runnerUp
            ? new Climb(this, runnerUp.Branch, new Dictionary<string, string?>(runnerUp.Worn, StringComparer.Ordinal))
            : null;

    /// <summary>The step under way in its pass: 0 for the weapon, then each slot, then the set bonuses.</summary>
    public int Step => _step;

    /// <summary>How many steps a pass makes with the weapon held now.</summary>
    public int StepsInPass => (_branch == null ? 2 : SlotSteps(_branch).Count + 2) + KindSteps;

    // The gear's steps in a pass: the weapon, then with one held each slot and the set bonuses.
    private int GearSteps => _branch == null ? 1 : SlotSteps(_branch).Count + 2;

    /// <summary>The pass being made, from 1.</summary>
    public int Passes { get; private set; }

    /// <summary>The set held now, as changes on current gear.</summary>
    public ChangeList Held { get; private set; } = ChangeList.Empty;

    public bool Done { get; private set; }

    /// <summary>
    /// The sets of the next step, the held one among them, each as changes on current gear; null when the climb
    /// is over. A step with nothing but the held set is passed over.
    /// </summary>
    public IReadOnlyList<ChangeList>? Next()
    {
        while (!Done)
        {
            _offered.Clear();
            if (_ranFrom.TryGetValue(_step, out var ran) && ran.Equals(Held))
            {
                Advance();
                continue;
            }
            if (_step >= GearSteps)
                OfferKind(_step - GearSteps);
            else if (_step == 0)
                foreach (var branch in _branches)
                    Offer(branch, Moved(branch));
            else if (_branch is { } held && _step - 1 < SlotSteps(held).Count)
            {
                // One slot at a time, the unit's others held: two ring slots over hundreds of rings would otherwise
                // be every pair of them in one step.
                var (unit, slot) = SlotSteps(held)[_step - 1];
                var options = unit.Pool.Cast<string?>().ToList();
                if (unit.AllowEmpty)
                    options.Add(null);
                foreach (var item in options)
                {
                    if (
                        item != null
                        && unit.Slots.Count(s => s != slot && _worn.GetValueOrDefault(s) == item)
                            >= unit.Available.GetValueOrDefault(item)
                    )
                        continue;
                    Offer(held, new Dictionary<string, string?>(_worn, StringComparer.Ordinal) { [slot] = item });
                }
            }
            else if (_branch is { } setsOf)
                foreach (var bonus in _catalogue.SetBonuses)
                foreach (var worn in Wearing(setsOf, bonus))
                    Offer(setsOf, worn);
            if (_offered.Keys.Any(set => !set.Equals(Held)))
            {
                _offered.TryAdd(Held, (_branch, _worn, _chosen));
                _ranFrom[_step] = Held;
                return [.. _offered.Keys];
            }
            Advance();
        }
        return null;
    }

    /// <summary>The weapon a set of the current step holds; null for none, or for one the step didn't offer.</summary>
    public string? WeaponOf(ChangeList set) =>
        _offered.TryGetValue(set, out var offered) ? offered.Branch?.Weapon : null;

    /// <summary>The style a set of the current step fights in; "" for one the step didn't offer.</summary>
    public string StyleOf(ChangeList set) =>
        _offered.TryGetValue(set, out var offered) ? offered.Branch?.Style ?? "" : "";

    /// <summary>
    /// Keeps the step's winner, one of the sets <see cref="Next"/> gave, and moves on. On the
    /// <see cref="WeaponStep"/>, <paramref name="runnerUp"/> is kept for <see cref="Restart"/>; on the prayers'
    /// singles step, <paramref name="ranking"/> (the step's sets, the best first) gives the pairs step its singles.
    /// </summary>
    public void Won(ChangeList winner, ChangeList? runnerUp = null, IReadOnlyList<ChangeList>? ranking = null)
    {
        ArgumentNullException.ThrowIfNull(winner);
        if (ranking != null)
            Ranked(ranking);
        if (!_offered.TryGetValue(winner, out var set))
            throw new ArgumentException("the winner is not one of the step's sets", nameof(winner));
        if (
            WeaponStep
            && runnerUp != null
            && !runnerUp.Equals(winner)
            && _offered.TryGetValue(runnerUp, out var second)
            && second.Branch is { } secondBranch
        )
            _runnerUp = (secondBranch, second.Worn);
        if (!winner.Equals(Held))
        {
            (_branch, _worn, _chosen) = set;
            Held = winner;
            _changed = true;
        }
        Advance();
    }

    // Every slot of the branch's units, in unit order: a step each.
    private static List<(Unit Unit, string Slot)> SlotSteps(Branch branch) =>
        [.. branch.Units.SelectMany(unit => unit.Slots.Select(slot => (unit, slot)))];

    private void Advance()
    {
        _step++;
        // The units' steps, then the set bonuses' step, then a step per kind of choice.
        if (_step < GearSteps + KindSteps)
            return;
        if (!_changed || Passes >= MostPasses)
        {
            Done = true;
            return;
        }
        Passes++;
        // A climb from the runner-up keeps its weapon: it is there to see where that weapon leads.
        _step = Restarted ? 1 : 0;
        _changed = false;
    }

    /// <summary>
    /// The most sets one bonus's step offers: every choice of pieces for each of its counts, which a set of many
    /// pieces could make many more of.
    /// </summary>
    public const int MostPerBonus = 64;

    // The held set with each choice of the bonus's pieces for every count the bonus gives something at (2 of 4, 3 of
    // 4, all 4...), each piece in a slot of a unit that offers it; a choice whose pieces don't all fit, or that
    // changes nothing, isn't offered. A bonus of one piece is the units' steps' to find.
    private IEnumerable<Dictionary<string, string?>> Wearing(Branch branch, GearSetBonus bonus)
    {
        var pieces = bonus
            .Items.Where(piece => branch.Units.Any(u => u.Pool.Contains(piece, StringComparer.Ordinal)))
            .ToList();
        var offered = 0;
        foreach (var count in bonus.Steps.Keys.Where(k => k >= 2 && k <= pieces.Count).Order())
        foreach (var choice in Choose(pieces, count))
        {
            if (offered >= MostPerBonus)
                yield break;
            if (Placed(branch, choice) is not { } worn || worn.All(w => _worn.GetValueOrDefault(w.Key) == w.Value))
                continue;
            offered++;
            yield return worn;
        }
    }

    // The held set with the pieces worn, each in the first free slot of a unit that offers it; null when one has none.
    private Dictionary<string, string?>? Placed(Branch branch, IReadOnlyList<string> pieces)
    {
        var worn = new Dictionary<string, string?>(_worn, StringComparer.Ordinal);
        var filled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var piece in pieces)
        {
            var slot = branch
                .Units.Where(u => u.Pool.Contains(piece, StringComparer.Ordinal))
                .SelectMany(u => u.Slots)
                .FirstOrDefault(s => !filled.Contains(s));
            if (slot == null)
                return null;
            filled.Add(slot);
            worn[slot] = piece;
        }
        return worn;
    }

    // Every way to take count of the items, in their order.
    private static IEnumerable<IReadOnlyList<string>> Choose(List<string> items, int count)
    {
        if (count == 0)
        {
            yield return [];
            yield break;
        }
        for (var first = 0; first <= items.Count - count; first++)
            foreach (var rest in Choose(items.Skip(first + 1).ToList(), count - 1))
                yield return [items[first], .. rest];
    }

    // A gear set with the choices held, those its weapon can't use put back to the setup's own.
    private void Offer(Branch branch, Dictionary<string, string?> worn)
    {
        var chosen = Fitting(branch, _chosen);
        _offered.TryAdd(With(Planner.Changes(_catalogue, branch, worn), chosen), (branch, worn, chosen));
    }

    // The held set on another weapon and stance: what the branch fixes, and in each unit what is held, unless the
    // weapon fires something else than the ammo held: then the unit's first choice.
    private Dictionary<string, string?> Moved(Branch branch)
    {
        var worn = new Dictionary<string, string?>(_worn, StringComparer.Ordinal);
        foreach (var (slot, item) in branch.Fixed)
            worn[slot] = item;
        var fires = branch.Weapon == null ? null : _catalogue.Item(branch.Weapon)?.AmmoNeeded;
        foreach (var unit in branch.Units)
        {
            var isAmmo =
                fires != null && unit.Pool.Count > 0 && unit.Pool.All(i => _catalogue.Item(i)?.AmmoType != null);
            if (
                !isAmmo
                || unit.Slots.All(s =>
                    worn.GetValueOrDefault(s) is { } held && _catalogue.Item(held)?.AmmoType == fires
                )
            )
                continue;
            var first = unit.Choices().First();
            for (var s = 0; s < unit.Slots.Count; s++)
                worn[unit.Slots[s]] = first[s];
        }
        return worn;
    }
}
