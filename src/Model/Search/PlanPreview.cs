using System.Numerics;

namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// What a search would fight, before it starts: the counts the start form shows. <see cref="Seconds"/> is null until
/// a search of this kind of target has been timed; <see cref="OverLimit"/> says the search will go a slot at a time
/// because fighting every set would take longer than the long-search limit, <see cref="LimitSeconds"/>, or, with
/// no search of the kind timed yet, because there are too many sets to risk it.
/// </summary>
public sealed record PlanPreview
{
    /// <summary>A setup search is planned for about this many passes over the choices: one, and one after a change.</summary>
    public const int KindPasses = 2;

    /// <summary>A search expected to take longer than this improves a slot at a time instead.</summary>
    public const double LongSearchSeconds = 150;

    /// <summary>
    /// The most sets fought whole before any search of the kind of target has been timed: past it the time could
    /// be anything, and a slot at a time is sure to end.
    /// </summary>
    public const int MostUntimed = 300;

    public SearchKind Kind { get; init; }
    public BigInteger Possible { get; init; }
    public BigInteger RuledOut { get; init; }
    public int ToFight { get; init; }
    public int LeftOut { get; init; }
    public bool Climb { get; init; }
    public bool NothingToSearch { get; init; }
    public IReadOnlyList<SkippedStyle> SkippedStyles { get; init; } = [];
    public double? Seconds { get; init; }
    public bool OverLimit { get; init; }
    public double LimitSeconds { get; init; }

    /// <summary>
    /// A setup search: the sets each kind adds to a pass, in the search's order, whether switched on or not; those
    /// switched on count towards <see cref="Seconds"/>.
    /// </summary>
    public IReadOnlyList<KindCount> Kinds { get; init; } = [];

    /// <summary>The kinds the request switched on.</summary>
    public IReadOnlyList<ChoiceKind> On { get; init; } = [];

    /// <summary>
    /// Whether the build has combat potions: without any the potion switch can't be used; with some, an empty
    /// potion kind means the character has none to try.
    /// </summary>
    public bool PotionsInBuild { get; init; }

    /// <summary>A setup search whose choices' steps are expected to take it past the long-search limit.</summary>
    public bool KindsOverLimit { get; init; }

    /// <summary>Droptimizer: the unowned items read and those wearable now.</summary>
    public int Unowned { get; init; }
    public int Wearable { get; init; }
}

/// <summary>
/// One kind's share of a setup search: <see cref="Options"/> to try one at a time, and <see cref="Pairs"/> (prayers
/// with two slots) tried after; <see cref="Slots"/> on at once.
/// </summary>
public sealed record KindCount(ChoiceKind Kind, string Name, int Options, int Pairs, int Slots)
{
    /// <summary>The sets the kind's steps fight in one pass, the held set apart.</summary>
    public int Sets => Options + Pairs;
}
