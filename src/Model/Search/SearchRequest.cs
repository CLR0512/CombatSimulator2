namespace MelvorMods.CombatSimulator2.Model.Search;

public enum SearchKind
{
    /// <summary>The best set among what the character owns.</summary>
    BestGear,

    /// <summary>Each unowned item alone in current gear.</summary>
    Droptimizer,
}

/// <summary>What a best-gear search chooses from.</summary>
public enum ItemPool
{
    /// <summary>What the character has.</summary>
    Owned,

    /// <summary>
    /// Every item in the game: one not owned is fought as if had, with its requirements ignored for it alone.
    /// </summary>
    Every,
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
    /// its requirements ignored for it alone. Gear it doesn't own comes in with <see cref="ItemPool.Every"/>, or one
    /// item at a time in Droptimizer ("Find upgrades"). A setup that ignores requirements lets owned locked gear in
    /// too, as its fights can wear it.
    /// </summary>
    public bool IncludeUnowned { get; init; }

    public ItemPool Pool { get; init; }

    /// <summary>The default longest a gear search a slot at a time may run: the player's setting starts here.</summary>
    public const double DefaultMaxSeconds = 900;

    /// <summary>
    /// The longest a gear search a slot at a time runs, the player's choice; it then keeps the best set found so far.
    /// Null runs it until its passes settle.
    /// </summary>
    public double? MaxSeconds { get; init; } = DefaultMaxSeconds;

    /// <summary>With every item: only items the setup's levels already meet.</summary>
    public bool WearableOnly { get; init; }

    /// <summary>With every item: only items owned, or with a source the character can reach now.</summary>
    public bool ReachedOnly { get; init; }

    /// <summary>With every item: sources by name; an item whose every source is one of them is left out.</summary>
    public IReadOnlySet<string> LeftOutSources { get; init; } = new HashSet<string>();

    /// <summary>Whether the pool is narrowed past every item.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Narrowed => WearableOnly || ReachedOnly || LeftOutSources.Count > 0;

    /// <summary>
    /// Why the request can't be searched against <paramref name="catalogue"/>; empty when it can. Whether anything
    /// is left to try is the plan's to say.
    /// </summary>
    public IReadOnlyList<Note> Check(GearCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var problems = new List<Note>(Refusals());
        foreach (var slot in Locks.Order(StringComparer.Ordinal))
            if (catalogue.Slots.All(s => s.Id != slot))
                problems.Add(Note.Of("No slot {0}", Ids.Brief(slot)));
        foreach (var item in Excluded.Order(StringComparer.Ordinal))
            if (catalogue.Item(item) == null)
                problems.Add(Note.Of("{0} is not an item that can be worn", Ids.Brief(item)));
        return problems;
    }

    /// <summary>
    /// Why the request can't be searched whatever the catalogue holds, so a search can refuse it before reading one.
    /// </summary>
    public IReadOnlyList<Note> Refusals()
    {
        var problems = new List<Note>();
        if (string.IsNullOrWhiteSpace(Target) || Target.Contains(' ', StringComparison.Ordinal))
            problems.Add(Note.Of("Choose one target"));
        if (Kind == SearchKind.Droptimizer && Locks.Count > 0)
            problems.Add(Note.Of("Droptimizer keeps every slot as worn: it takes no locks"));
        if (Kind == SearchKind.Droptimizer && (Kinds.Count > 0 || IncludeUnowned))
            problems.Add(Note.Of("Droptimizer tries gear only"));
        if (Pool == ItemPool.Every && Kind == SearchKind.Droptimizer)
            problems.Add(Note.Of("Droptimizer tries every item already"));
        if (Pool == ItemPool.Every && (Ids.IsMaster(Target) || Ids.IsSet(Target)))
            problems.Add(Note.Of("Every item: one target at a time"));
        if (Pool == ItemPool.Owned && Narrowed)
            problems.Add(Note.Of("Narrowing the pool needs every item"));
        foreach (var kind in Kinds.Where(k => !Enum.IsDefined(k)))
            problems.Add(Note.Of("No kind of choice {0}", (int)kind));
        return problems;
    }
}
