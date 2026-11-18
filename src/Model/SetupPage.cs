namespace MelvorMods.CombatSimulator2.Model;

/// <summary>Where a setup page is listed.</summary>
public enum PageSection
{
    Character,
    Skills,
    Account,
}

public enum PageState
{
    Changeable,

    /// <summary>Shown from the save; the game has no call CS2 could change it with.</summary>
    ReadOnly,

    /// <summary>The build has no data for it: a greyed placeholder.</summary>
    NoData,
}

/// <summary>How an entry is edited.</summary>
public enum EntryKind
{
    Toggle,
    Count,
    Rank,

    /// <summary>Shown only.</summary>
    Info,
}

/// <summary>
/// One thing on a page: a star, a rank, a purchase, a quest. <c>Value</c> is the side's, <c>SaveValue</c> the
/// character's before the side's changes, <c>Max</c> the most the game allows. <c>Change</c> is the key an edit
/// makes, null when the entry can't be changed, and then <c>Fixed</c> says why. <c>Notable</c>: it carries
/// modifiers or unlocks something a fight uses, so a page's short list and a setup's line keep it. <c>Requires</c>
/// holds what the side doesn't meet; <c>After</c>, met or not, what hangs on other entries of the page, so a page
/// can show it from the side's values as they are edited, before the side is checked again. <c>Description</c> is
/// the game's own text for it, where it has one. <c>Names</c> gives the entry's name at each value where that
/// changes with it (a guild's rank), so the name follows an edit at once. <c>Remark</c> is a fact beside the entry
/// that neither locks nor fixes it ("Not in your bank").
/// </summary>
public sealed record PageEntry(
    string Id,
    string Name,
    string? Icon,
    EntryKind Kind,
    int Value,
    int Max,
    int SaveValue,
    IReadOnlyList<Note> Requires,
    IReadOnlyList<ModifierLine> Modifiers,
    ChangeKey? Change,
    Note? Fixed,
    bool Notable = true,
    IReadOnlyList<EntryNeed>? After = null,
    string? Description = null,
    IReadOnlyList<string>? Names = null,
    Note? Remark = null
);

/// <summary>
/// A requirement met once the page's entry at <c>On</c> is at <c>AtLeast</c>: a quest done, a guild at a rank.
/// </summary>
public sealed record EntryNeed(Note Text, ChangeKey On, int AtLeast = 1);

/// <summary>
/// Entries the game groups together: a constellation, a guild, a shop category. <c>Heading</c> names a group CS2
/// makes itself (pets in combat and the others), in place of <c>Name</c>, the game's own.
/// </summary>
public sealed record PageGroup(
    string Id,
    string Name,
    string? Icon,
    Note? Summary,
    IReadOnlyList<PageEntry> Entries,
    Note? Heading = null
);

/// <summary>
/// One page of a setup, read from the game's data and a copy of the side. <c>Reason</c> says why a page isn't
/// changeable, or on a changeable page what the whole page is about. <c>Gate</c> holds the game's requirement texts for a page the build keeps from the player (empty
/// when open): a change on a gated page is refused while requirements are checked.
/// </summary>
public sealed record SetupPage(
    string Id,
    PageSection Section,
    PageState State,
    Note? Reason,
    IReadOnlyList<Note> Gate,
    IReadOnlyList<PageGroup> Groups
)
{
    /// <summary>
    /// What <paramref name="entry"/> still needs, reading the page's other entries through <paramref name="value"/>:
    /// the side's values as they are being edited, or as its last check read them.
    /// </summary>
    public IReadOnlyList<Note> Unmet(PageEntry entry, Func<PageEntry, int> value)
    {
        if (entry.After is not { Count: > 0 } after)
            return entry.Requires;
        var needs = entry.Requires.ToList();
        foreach (var need in after)
        {
            var other = Groups.SelectMany(g => g.Entries).FirstOrDefault(e => e.Change == need.On);
            if (other == null || value(other) < need.AtLeast)
                needs.Add(need.Text);
        }
        return needs;
    }

    public const string Gear = "gear";
    public const string Astrology = "astrology";
    public const string Guild = "guild";
    public const string Slayer = "slayer";
    public const string Agility = "agility";
    public const string Herblore = "herblore";
    public const string Firemaking = "firemaking";
    public const string Shop = "shop";
    public const string Quests = "quests";
    public const string Pets = "pets";

    /// <summary>Every page, in the order the page list shows them.</summary>
    public static readonly IReadOnlyList<(string Id, PageSection Section)> All =
    [
        (Gear, PageSection.Character),
        (Astrology, PageSection.Skills),
        (Guild, PageSection.Skills),
        (Slayer, PageSection.Skills),
        (Agility, PageSection.Skills),
        (Herblore, PageSection.Skills),
        (Firemaking, PageSection.Skills),
        (Shop, PageSection.Account),
        (Quests, PageSection.Account),
        (Pets, PageSection.Account),
    ];

    public bool Gated => Gate.Count > 0;

    /// <summary>The page a kind of change is made on.</summary>
    public static string PageOf(ChangeKind kind) =>
        kind switch
        {
            ChangeKind.Star => Astrology,
            ChangeKind.Guild => Guild,
            ChangeKind.Shop => Shop,
            ChangeKind.Quest => Quests,
            ChangeKind.Blocks or ChangeKind.Loadouts or ChangeKind.SlayerTask or ChangeKind.SlayerTaskFor => Slayer,
            ChangeKind.Potion => Herblore,
            ChangeKind.Pet => Pets,
            ChangeKind.Bonfire => Firemaking,
            ChangeKind.Technique => Agility,
            _ => Gear,
        };

    /// <summary>How many of <paramref name="changes"/> are made on the page <paramref name="pageId"/>.</summary>
    public static int ChangeCount(string pageId, ChangeList changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return changes.Count(c => PageOf(c.Kind) == pageId);
    }

    /// <summary><paramref name="changes"/> without the ones made on the page <paramref name="pageId"/>.</summary>
    public static ChangeList WithoutPage(string pageId, ChangeList changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return changes.Where(c => PageOf(c.Kind) == pageId).Aggregate(changes, (kept, c) => kept.Without(c.Key));
    }
}
