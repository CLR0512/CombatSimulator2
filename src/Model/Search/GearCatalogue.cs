using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>Whether more of a modifier is good for the character, as the game labels it.</summary>
public enum Direction
{
    Good,
    Bad,

    /// <summary>The game says neither: an item carrying one is never compared.</summary>
    Neutral,
}

/// <summary>
/// One modifier an item or a set step gives: the stat it changes, its condition as the game words it (null when
/// it always holds), and whether that condition holds for this setup and target.
/// </summary>
public sealed record PartModifier(
    string Stat,
    string? Context,
    double Value,
    Direction Direction,
    PartGroup Group,
    bool InForce = true
);

/// <summary>A weapon's own part: its type, the style and stances that type gives, and what it fires.</summary>
public sealed record WeaponPart(
    string Type,
    string Style,
    IReadOnlyList<string> Stances,
    string? DamageType = null,
    IReadOnlyList<string>? Elements = null,
    string? AmmoType = null
);

/// <summary>An effect an item carries: its kind and what sets it off.</summary>
public sealed record ItemEffect(string Kind, string Trigger);

/// <summary>An equipment slot: its type decides which items fit; the grid place is where the game draws it.</summary>
public sealed record GearSlot(string Id, string Type, bool Unlocked = true, int Column = 0, int Row = 0);

/// <summary>
/// An item that can be worn, read as its parts. <see cref="Stats"/> are better higher; anything that isn't is
/// read as a modifier with its direction. <see cref="Unmet"/> is empty
/// when the setup may wear it. <see cref="Opaque"/> items have a part the search has no rule for: they are never
/// compared, only fought.
/// </summary>
public sealed record GearItem(string Id, string Name, string SlotType)
{
    /// <summary>The slots it takes beside the one it is put in.</summary>
    public IReadOnlyList<string> Occupies { get; init; } = [];

    /// <summary>How many the character has: in the bank, worn and in other loadouts.</summary>
    public int Owned { get; init; }

    /// <summary>The slot it is worn in now; null when it isn't.</summary>
    public string? Worn { get; init; }
    public IReadOnlyList<Note> Unmet { get; init; } = [];
    public IReadOnlyDictionary<string, double> Stats { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<PartModifier> Bonuses { get; init; } = [];

    /// <summary>The special attacks it gives, by id.</summary>
    public IReadOnlyList<string> Attacks { get; init; } = [];
    public IReadOnlyList<ItemEffect> Effects { get; init; } = [];

    /// <summary>The triggers that use one up; empty when it lasts.</summary>
    public IReadOnlyList<string> ConsumedOn { get; init; } = [];

    /// <summary>The set bonuses it counts towards.</summary>
    public IReadOnlyList<string> Sets { get; init; } = [];
    public WeaponPart? Weapon { get; init; }

    /// <summary>The kind of ammo it is; null when it isn't ammo.</summary>
    public string? AmmoType { get; init; }

    /// <summary>Whether one slot holds a quantity of it.</summary>
    public bool Stackable { get; init; }
    public bool Opaque { get; init; }

    /// <summary>Where the item comes from, as the game's data names it; null when the data doesn't say.</summary>
    public Note? Source { get; init; }

    /// <summary>How the item is got from <see cref="Source"/>, in full: "Dropped by …", "Opened from …".</summary>
    public Note? SourceDetail { get; init; }

    /// <summary>Every way to get it, by name, the first being <see cref="Source"/>'s.</summary>
    public IReadOnlyList<string> SourceNames { get; init; } = [];

    /// <summary>
    /// Whether the character can reach one of its sources now, by the game's own checks; null when none can and some
    /// can't be judged.
    /// </summary>
    public bool? Reachable { get; init; }

    /// <summary>Which part made it opaque, for the counts and <c>cs2 deps</c>.</summary>
    public string? OpaqueReason { get; init; }

    /// <summary>Every part the search has no rule for; the first is <see cref="OpaqueReason"/>.</summary>
    public IReadOnlyList<string> Unknowns { get; init; } = [];

    public bool Wearable => Unmet.Count == 0;

    /// <summary>
    /// The ammo it needs in another slot; null when it needs none, or when it is the ammo it fires (a javelin thrown
    /// from its own stack).
    /// </summary>
    public string? AmmoNeeded => Weapon?.AmmoType is { } fires && fires != AmmoType ? fires : null;

    /// <summary>Whether it brings anything a comparison of stats and modifiers can't see.</summary>
    public bool Special => Opaque || Attacks.Count > 0 || Effects.Count > 0;
}

/// <summary>A set bonus: its items, and the modifiers each count of them worn gives.</summary>
public sealed record GearSetBonus(
    string Id,
    string Name,
    IReadOnlyList<string> Items,
    IReadOnlyDictionary<int, IReadOnlyList<PartModifier>> Steps
)
{
    /// <summary>
    /// The parts of its steps the search has no rule for. Its pieces are safe all the same: they only compare with
    /// pieces of the same set.
    /// </summary>
    public IReadOnlyList<string> Unknowns { get; init; } = [];
}

/// <summary>
/// Everything a search chooses from, read once from a copy prepared with the setup: the slots, every wearable
/// item as its parts, the set bonuses, and what is worn now.
/// </summary>
public sealed record GearCatalogue
{
    public IReadOnlyList<GearSlot> Slots { get; init; } = [];
    public IReadOnlyList<GearItem> Items { get; init; } = [];
    public IReadOnlyList<GearSetBonus> SetBonuses { get; init; } = [];

    /// <summary>Each slot's item now; a slot without one is absent.</summary>
    public IReadOnlyDictionary<string, string> Current { get; init; } = new Dictionary<string, string>();
    public string Stance { get; init; } = "";

    /// <summary>Whether the target can hurt the character at all, from its data.</summary>
    public bool TargetAttacks { get; init; } = true;

    /// <summary>The group of each stat in <see cref="GearItem.Stats"/>.</summary>
    public IReadOnlyDictionary<string, PartGroup> StatGroups { get; init; } = new Dictionary<string, PartGroup>();

    /// <summary>
    /// The equipment stats each stance reads on the one who attacks in it: its accuracy stat, what its style's max
    /// hit is worked out from, and the style's interval, ward, armour and lifesteal stats. A stat some stance reads
    /// does nothing for a set fighting in a stance that doesn't.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> StanceReads { get; init; } =
        new Dictionary<string, IReadOnlySet<string>>();

    /// <summary>Every stat some stance reads on the one attacked: the evasion stats.</summary>
    public IReadOnlySet<string> EvasionStats { get; init; } = new HashSet<string>();

    /// <summary>
    /// The evasion stats the target's own stances read on the character; null when they couldn't be read, and
    /// every evasion stat then counts.
    /// </summary>
    public IReadOnlySet<string>? TargetReads { get; init; }

    /// <summary>
    /// Whether an equipment stat can change a fight in <paramref name="stance"/> against the target: one that
    /// stances read counts only when this stance reads it, an evasion stat only when the target's stances read it,
    /// and a stat nothing is known to read always counts.
    /// </summary>
    public bool Reads(string stat, string? stance)
    {
        var offence = StanceReads.Values.Any(reads => reads.Contains(stat));
        var evasion = EvasionStats.Contains(stat);
        if (!offence && !evasion)
            return true;
        return (offence && (stance == null || !StanceReads.TryGetValue(stance, out var reads) || reads.Contains(stat)))
            || (evasion && (TargetReads == null || TargetReads.Contains(stat)));
    }

    /// <summary>The levels the items' requirements read, by skill.</summary>
    public IReadOnlyDictionary<string, int> Levels { get; init; } = new Dictionary<string, int>();
    public string Build { get; init; } = "";

    /// <summary>Styles the setup can't fight in, with why: magic without a spell.</summary>
    public IReadOnlyDictionary<string, Note> UnusableStyles { get; init; } = new Dictionary<string, Note>();

    public GearItem? Item(string id) => Items.FirstOrDefault(i => i.Id == id);

    /// <summary>
    /// The equip changes of <paramref name="changes"/> that put on an item the character can't wear yet: a search
    /// fights them with that item's requirements ignored.
    /// </summary>
    public ChangeList Locked(IEnumerable<Change> changes) =>
        ChangeList.Of(
            changes.Where(c => c.Kind == ChangeKind.Equip && c.Item is { } id && Item(id) is { Wearable: false })
        );

    /// <summary>
    /// What is owned and worn, as one short hash: it changes when what a search could choose does. Counts only go as
    /// far as a search can use them: ammo used up in play, or a second copy of an item that fits one slot, leaves it
    /// as it was.
    /// </summary>
    public string GearHash =>
        Short(
            Items
                .Where(i => i.Owned > 0)
                .OrderBy(i => i.Id, StringComparer.Ordinal)
                .Select(i => string.Create(CultureInfo.InvariantCulture, $"{i.Id}={Usable(i)}"))
                .Concat(Current.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key}:{s.Value}"))
                .Append("stance:" + Stance)
        );

    // How many of an item a set could wear: one of a stack, and no more than the slots it fits.
    private int Usable(GearItem item) =>
        item.Stackable ? 1 : Math.Min(item.Owned, Math.Max(1, Slots.Count(s => s.Type == item.SlotType)));

    /// <summary>The levels requirements read, as one short hash.</summary>
    public string LevelsHash =>
        Short(
            Levels
                .OrderBy(l => l.Key, StringComparer.Ordinal)
                .Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.Key}={l.Value}"))
        );

    /// <summary>Gear, levels and the game's build together: equal catalogues give a search the same choices.</summary>
    public string Hash => Short([GearHash, LevelsHash, Build]);

    private static string Short(IEnumerable<string> lines) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))))[..12];
}
