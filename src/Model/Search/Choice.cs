namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// A kind of choice a setup search may try beside gear and stance, which it always searches. The order here is the
/// order the search steps through them.
/// </summary>
public enum ChoiceKind
{
    Prayers,
    Spell,
    Imbuement,
    Food,
    Toolbelt,
    Potion,
}

public static class ChoiceKinds
{
    /// <summary>On until the player switches them off: food never won where it was tried, so it starts off.</summary>
    public static readonly IReadOnlyList<ChoiceKind> Defaults =
    [
        ChoiceKind.Prayers,
        ChoiceKind.Spell,
        ChoiceKind.Imbuement,
        ChoiceKind.Toolbelt,
    ];

    public static readonly IReadOnlyList<ChoiceKind> All = Enum.GetValues<ChoiceKind>();

    /// <summary>The change kind that sets a choice of this kind on a setup.</summary>
    public static ChangeKind Change(ChoiceKind kind) =>
        kind switch
        {
            ChoiceKind.Prayers => ChangeKind.Prayer,
            ChoiceKind.Spell => ChangeKind.Spell,
            ChoiceKind.Imbuement => ChangeKind.Imbuement,
            ChoiceKind.Food => ChangeKind.Food,
            ChoiceKind.Toolbelt => ChangeKind.Toolbelt,
            _ => ChangeKind.Potion,
        };

    /// <summary>The kind as commands and settings write it: <c>prayers</c>, <c>spell</c>, …</summary>
    public static string Name(ChoiceKind kind) => kind.ToString().ToLowerInvariant();

    public static bool TryParse(string text, out ChoiceKind kind)
    {
        foreach (var each in All)
        {
            if (Name(each) == text)
            {
                kind = each;
                return true;
            }
        }
        kind = default;
        return false;
    }

    /// <summary>Kinds in canonical order, each once.</summary>
    public static IReadOnlyList<ChoiceKind> Normal(IEnumerable<ChoiceKind> kinds) => [.. kinds.Distinct().Order()];
}

/// <summary>
/// One option of a kind, ready to hold in a candidate. <see cref="Ids"/> are the game ids it puts on (two for a pair
/// of prayers; none for "none"); <see cref="Changes"/> put it on the setup from the setup's own choice. Food and
/// toolbelt items are <see cref="Owned"/> when the bank holds some; prayers, spells and imbuements always are.
/// <see cref="Unmet"/> are its unmet requirements' notes. <see cref="Needs"/> are the elements one of which the
/// weapon must give (spells and imbuements), and <see cref="NeedsMagic"/> asks for a Magic stance (spells).
/// <see cref="Opaque"/> carries a part the search can't rule on, so it is fought, never ruled out;
/// <see cref="OutsideCombat"/> acts only outside combat, so it is never a candidate.
/// </summary>
public sealed record ChoiceOption(ChoiceKind Kind, IReadOnlyList<string> Ids, ChangeList Changes)
{
    public bool Owned { get; init; } = true;
    public IReadOnlyList<Note> Unmet { get; init; } = [];
    public IReadOnlyList<string> Needs { get; init; } = [];
    public bool NeedsMagic { get; init; }
    public bool Opaque { get; init; }
    public bool OutsideCombat { get; init; }

    /// <summary>The game's names for <see cref="Ids"/>, in the same order.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>The one id, or null for none or a pair.</summary>
    public string? Id => Ids.Count == 1 ? Ids[0] : null;

    public bool IsNone => Ids.Count == 0;

    /// <summary>Whether it is the character's to use now, with nothing missing.</summary>
    public bool Usable => Owned && Unmet.Count == 0;

    /// <summary>
    /// Whether a search may try it: never when it acts only outside combat; otherwise when the character can use it,
    /// or when the search includes what the character doesn't have.
    /// </summary>
    public bool IsCandidate(bool includeUnowned) => !OutsideCombat && (Usable || includeUnowned);

    /// <summary>Whether held gear allows it: one of its elements on the weapon, and a Magic stance for a spell.</summary>
    public bool Allowed(IReadOnlyCollection<string> elements, bool magicStance) =>
        (Needs.Count == 0 || Needs.Any(elements.Contains)) && (!NeedsMagic || magicStance);
}

/// <summary>
/// A kind's options for one search. <see cref="Subject"/> is the toolbelt slot for a toolbelt choice (one choice per
/// slot), else null. <see cref="Current"/> is the setup's own option. <see cref="Slots"/> is how many can be on at
/// once: 1 or 2 prayers by the Prayer level, 1 otherwise.
/// </summary>
public sealed record Choice(
    ChoiceKind Kind,
    string? Subject,
    ChoiceOption Current,
    IReadOnlyList<ChoiceOption> Options,
    int Slots = 1
)
{
    /// <summary>Its name in replies: the kind, or the toolbelt slot.</summary>
    public string Name => Subject is { } slot ? Ids.Brief(slot) : ChoiceKinds.Name(Kind);
}
