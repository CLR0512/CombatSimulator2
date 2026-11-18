namespace MelvorMods.CombatSimulator2.Model.Search;

public enum SearchKind
{
    /// <summary>The best set among what the character owns.</summary>
    BestGear,

    /// <summary>Each unowned item alone in current gear.</summary>
    Droptimizer,
}

/// <summary>
/// One search: the setup it starts from, one target (its key), the goal, and how its sets are fought.
/// <see cref="Trials"/> is the last stage's count. A null <see cref="Seed"/> takes a new one. <see cref="SlotAtATime"/>
/// improves a slot at a time from the start.
/// </summary>
public sealed record SearchRequest(SearchKind Kind, Setup Setup, string Target, Goal Goal, int Trials, int Copies)
{
    /// <summary>Slots kept as worn.</summary>
    public IReadOnlySet<string> Locks { get; init; } = new HashSet<string>();

    /// <summary>Items never used.</summary>
    public IReadOnlySet<string> Excluded { get; init; } = new HashSet<string>();

    /// <summary>Every style the character can use, not only the setup weapon's.</summary>
    public bool AllStyles { get; init; }
    public ulong? Seed { get; init; }

    /// <summary><c>host</c>, <c>game</c>, or null for the setting.</summary>
    public string? Where { get; init; }
    public TrialLimits? Limits { get; init; }
    public bool SlotAtATime { get; init; }

    /// <summary>The kinds of choice tried besides gear and stance; none searches gear alone.</summary>
    public IReadOnlyList<ChoiceKind> Kinds { get; init; } = [];

    /// <summary>
    /// Also tries choices the character doesn't own or can't use yet, and gear it owns but can't wear yet, each with
    /// its requirements ignored for it alone. Gear it doesn't own is Droptimizer's ("Find upgrades"), one item at a
    /// time. Only this lets locked gear in: the setup ignoring requirements doesn't by itself, so one switch decides.
    /// </summary>
    public bool IncludeUnowned { get; init; }

    /// <summary>
    /// Why the request can't be searched against <paramref name="catalogue"/>; empty when it can. Whether anything
    /// is left to try is the plan's to say.
    /// </summary>
    public IReadOnlyList<Note> Check(GearCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var problems = new List<Note>();
        if (string.IsNullOrWhiteSpace(Target) || Target.Contains(' ', StringComparison.Ordinal))
            problems.Add(Note.Of("Choose one target"));
        if (Kind == SearchKind.Droptimizer && Locks.Count > 0)
            problems.Add(Note.Of("Droptimizer keeps every slot as worn: it takes no locks"));
        if (Kind == SearchKind.Droptimizer && (Kinds.Count > 0 || IncludeUnowned))
            problems.Add(Note.Of("Droptimizer tries gear only"));
        foreach (var kind in Kinds.Where(k => !Enum.IsDefined(k)))
            problems.Add(Note.Of("No kind of choice {0}", (int)kind));
        foreach (var slot in Locks.Order(StringComparer.Ordinal))
            if (catalogue.Slots.All(s => s.Id != slot))
                problems.Add(Note.Of("No slot {0}", Ids.Brief(slot)));
        foreach (var item in Excluded.Order(StringComparer.Ordinal))
            if (catalogue.Item(item) == null)
                problems.Add(Note.Of("{0} is not an item that can be worn", Ids.Brief(item)));
        return problems;
    }
}
