namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>What a plan is asked for: the catalogue alone, the plan's counts, or the plan a search fights.</summary>
public enum PlanWhat
{
    /// <summary>The catalogue alone, to tell whether a kept answer is still current.</summary>
    Catalogue,

    /// <summary>The plan as every set would be fought, for counts before a search starts.</summary>
    Counts,

    /// <summary>The plan a search fights: equal sets merged, and a slot at a time when it would take too long.</summary>
    Search,
}

/// <summary>
/// A plan read from a copy prepared with the setup: the catalogue, and, unless only it was asked for, the plan or
/// Droptimizer's, or the problem the request has with the catalogue. Read in the simulator host or in the game.
/// </summary>
public sealed record PlanReply(GearCatalogue Catalogue)
{
    public SearchPlan? Plan { get; init; }
    public DropPlan? Drops { get; init; }
    public Note? Problem { get; init; }

    /// <summary>
    /// Every kind's choices beyond gear, read when the request switches any kind on; the search tries those it
    /// switches on, and reads the rest for names (food apart from other supplies).
    /// </summary>
    public IReadOnlyList<Choice> Choices { get; init; } = [];

    /// <summary>What the choices offer the character, as one short hash: an answer's key holds it.</summary>
    public string? Supplies { get; init; }
}
