namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// The choices beyond gear a setup search tries, one step each after the gear's in every pass, and whether options
/// the character doesn't have are among them.
/// </summary>
public sealed record ChoiceSteps(IReadOnlyList<Choice> Choices, bool IncludeUnowned);

// A setup search's steps beyond gear: after the weapon, slots and set bonuses, each choice in turn with the gear
// held; prayers one at a time, then, with two slots, pairs of the best singles. A gear set that its weapon can't
// cast the held spell or imbuement with holds the setup's own instead, and a magic weapon without one it can cast
// fights with the strongest it can.
public sealed partial class Climb
{
    private readonly ChoiceSteps? _kinds;
    private Dictionary<string, ChoiceOption> _chosen;

    // The prayers' singles step's options, the best first, which its pairs step pairs.
    private List<ChoiceOption> _rankedSingles = [];

    /// <summary>
    /// A climb from a gear set already found (a race over every set), starting at the choices' steps: its later
    /// passes climb the gear again with the choices held, if a choice changed.
    /// </summary>
    public static Climb FromSet(
        GearCatalogue catalogue,
        IReadOnlyList<Branch> branches,
        ChangeList set,
        ChoiceSteps kinds
    )
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(kinds);
        return new Climb(catalogue, branches, kinds, set);
    }

    private Climb(GearCatalogue catalogue, IReadOnlyList<Branch> branches, ChoiceSteps kinds, ChangeList set)
        : this(catalogue, branches, kinds)
    {
        foreach (var change in set.Where(c => c.Kind == ChangeKind.Equip))
            _worn[change.Subject!] = change.Item;
        var weapon = _worn.Values.FirstOrDefault(i => i != null && catalogue.Item(i)?.Weapon != null);
        var stance = set.FirstOrDefault(c => c.Kind == ChangeKind.Stance)?.Value ?? catalogue.Stance;
        _branch = branches.FirstOrDefault(b => b.Weapon == weapon && b.Stance == stance) ?? _branch;
        // A choice the set already holds, such as a magic set's spell, is held as that option.
        foreach (var choice in kinds.Choices)
            if (
                choice.Options.FirstOrDefault(o =>
                    !o.Changes.IsEmpty && o.Changes.All(c => set.Find(c.Key)?.Value == c.Value)
                ) is
                { } held
            )
                _chosen[choice.Name] = held;
        Held = set;
        _step = GearSteps;
    }

    /// <summary>The option held now for each choice, by its name.</summary>
    public IReadOnlyDictionary<string, ChoiceOption> Chosen => _chosen;

    /// <summary>
    /// The changes of a set of the current step that put on something the character doesn't have or can't use yet:
    /// tried with their requirements ignored.
    /// </summary>
    public ChangeList IgnoringOf(ChangeList set) =>
        _offered.TryGetValue(set, out var offered)
            ? ChangeList.Of(offered.Chosen.Values.Where(o => !o.Usable).SelectMany(o => o.Changes))
            : ChangeList.Empty;

    /// <summary>
    /// A set of the current step as a candidate at <paramref name="index"/>: its style, and its changes to fight with
    /// requirements ignored.
    /// </summary>
    public Candidate CandidateOf(int index, ChangeList set) =>
        new(index, set, StyleOf(set)) { Ignoring = ChangeList.Of([.. IgnoringOf(set), .. _catalogue.Locked(set)]) };

    private static Dictionary<string, ChoiceOption> Unchosen(ChoiceSteps? kinds) =>
        kinds?.Choices.ToDictionary(c => c.Name, c => c.Current, StringComparer.Ordinal)
        ?? new Dictionary<string, ChoiceOption>(StringComparer.Ordinal);

    private List<(Choice Choice, bool Pairs)> KindStepList =>
        _kinds == null
            ? []
            :
            [
                .. _kinds.Choices.SelectMany(c =>
                    c.Kind == ChoiceKind.Prayers && c.Slots >= 2 ? new[] { (c, false), (c, true) } : [(c, false)]
                ),
            ];

    private int KindSteps => KindStepList.Count;

    // A kind's step: the held set with each of its options in turn.
    private void OfferKind(int index)
    {
        var (choice, pairs) = KindStepList[index];
        var (elements, magic) = Weapon(_branch);
        var held = _chosen[choice.Name];
        IEnumerable<ChoiceOption> options = pairs
            ? Choices
                .Pairs(choice, _rankedSingles)
                .Where(o => o.IsCandidate(_kinds!.IncludeUnowned) && o.Allowed(elements, magic))
            : Choices
                .For(choice, elements, magic, _kinds!.IncludeUnowned)
                .Where(o => choice.Kind != ChoiceKind.Prayers || o.Ids.Count <= 1)
                .Append(choice.Current);
        var gear = _branch is { } branch ? Planner.Changes(_catalogue, branch, _worn) : Held;
        foreach (var option in options.Where(o => !Choices.Same(o, held)))
        {
            var chosen = new Dictionary<string, ChoiceOption>(_chosen, StringComparer.Ordinal)
            {
                [choice.Name] = option,
            };
            _offered.TryAdd(With(Without(gear), chosen), (_branch, _worn, chosen));
        }
    }

    // Keeps the singles step's ranking for its pairs step.
    private void Ranked(IReadOnlyList<ChangeList> order)
    {
        var step = _step - GearSteps;
        if (step < 0 || step >= KindSteps || KindStepList[step] is not ({ Kind: ChoiceKind.Prayers } choice, false))
            return;
        _rankedSingles =
        [
            .. order
                .Where(_offered.ContainsKey)
                .Select(set => _offered[set].Chosen[choice.Name])
                .Where(o => o.Ids.Count == 1),
        ];
    }

    // The elements the branch's weapon casts with, and whether its stance is a Magic one.
    private (IReadOnlyCollection<string> Elements, bool Magic) Weapon(Branch? branch)
    {
        var weapon = branch?.Weapon is { } id ? _catalogue.Item(id)?.Weapon : null;
        return (weapon?.Elements ?? [], branch?.Style == Choices.Magic);
    }

    // The choices held, with a spell or imbuement the branch can't use put back to the setup's own.
    private Dictionary<string, ChoiceOption> Fitting(Branch branch, Dictionary<string, ChoiceOption> chosen)
    {
        if (_kinds == null)
            return chosen;
        var (elements, magic) = Weapon(branch);
        Dictionary<string, ChoiceOption>? fitting = null;
        foreach (var choice in _kinds.Choices)
        {
            if (!chosen.TryGetValue(choice.Name, out var option))
                continue;
            // A magic weapon needs a spell to fight with: one it can't cast, or none, gives way to the strongest it can.
            var needsSpell =
                magic && choice.Kind == ChoiceKind.Spell && (option.IsNone || !option.Allowed(elements, magic));
            if (!needsSpell && option.Allowed(elements, magic))
                continue;
            fitting ??= new Dictionary<string, ChoiceOption>(chosen, StringComparer.Ordinal);
            fitting[choice.Name] = needsSpell
                ? Choices.SpellFor(choice, elements, _kinds.IncludeUnowned) ?? choice.Current
                : choice.Current;
        }
        return fitting ?? chosen;
    }

    // The gear's changes with the choices' changes on them.
    private static ChangeList With(ChangeList gear, Dictionary<string, ChoiceOption> chosen)
    {
        var set = gear;
        foreach (var change in chosen.Values.SelectMany(o => o.Changes))
            set = set.With(change);
        return set;
    }

    // A set's gear and stance alone.
    private static ChangeList Without(ChangeList set) =>
        ChangeList.Of(set.Where(c => c.Kind is ChangeKind.Equip or ChangeKind.Stance));
}
