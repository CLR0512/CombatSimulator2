namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// One set to fight: changes on the search's base (gear and stance, and the other kinds a setup search tries), none
/// for the setup as it is. <see cref="Same"/>
/// names the sets ruled equal to it, which share its result. <see cref="StatsKey"/> is the stats the game gives it
/// against the target, when they were read.
/// </summary>
public sealed record Candidate(int Index, ChangeList Changes, string Style)
{
    public IReadOnlyList<ChangeList> Same { get; init; } = [];
    public string? StatsKey { get; init; }

    /// <summary>
    /// Its changes to options the character doesn't have or can't use yet, applied with their requirements ignored
    /// when the search includes them.
    /// </summary>
    public ChangeList Ignoring { get; init; } = ChangeList.Empty;

    public bool IsCurrent => Changes.IsEmpty;
}
