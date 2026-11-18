using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

// Changes tried against a setup for the simulation only. The model knows their form; the game decides, in a copy,
// whether each one applies.

/// <summary>What a change is to. The order here is the canonical order and the order changes are applied in.</summary>
public enum ChangeKind
{
    Level,

    /// <summary>Before the shop: a purchase can need a quest.</summary>
    Quest,

    /// <summary>Before the toolbelt: a toolbelt slot can be a purchase.</summary>
    Shop,
    Guild,
    Star,
    Equip,
    Toolbelt,

    /// <summary>After the gear: a new weapon brings its own stance, which the change then replaces.</summary>
    Stance,

    /// <summary>After the gear and stance: a staff gives the elements a spell needs.</summary>
    Spell,
    Imbuement,
    Food,
    Potion,
    Prayer,

    /// <summary>A Slayer master's blocked enemies: last, as nothing else needs them first.</summary>
    Blocks,

    /// <summary>The saved loadout each of a master's monsters is fought with; the run plan applies it, not a copy.</summary>
    Loadouts,

    /// <summary>Whether single monsters are fought on a slayer task: <c>task=on|off</c>; absent, as the save.</summary>
    SlayerTask,

    /// <summary>One monster's own slayer task choice, over the side's.</summary>
    SlayerTaskFor,

    /// <summary>A pet's bonuses added or taken away: <c>pet=&lt;pet&gt;:on|off</c>.</summary>
    Pet,

    /// <summary>The bonfire's tier, lit, or the bonfire put out: <c>bonfire=&lt;tier&gt;|out</c>.</summary>
    Bonfire,

    /// <summary>An Agility technique's level: <c>technique=&lt;technique&gt;:&lt;level&gt;</c>.</summary>
    Technique,
}

/// <summary>A list holds one change per key: the kind and its subject.</summary>
public readonly record struct ChangeKey(ChangeKind Kind, string? Subject);

/// <summary>
/// One change, with full game ids: a level (<see cref="Subject"/> a skill id, <see cref="Value"/> an integer), a
/// quest (a quest id; <c>done</c> or <c>open</c>), a shop purchase (a purchase id; how many are bought), a mastery
/// guild's rank (the guild's skill id; a rank index), an astrology star (a node id; <c>on</c>), an equipment or
/// toolbelt slot (a slot id; an item id or <c>none</c>), the attack stance or the spell (no subject; its id), the
/// imbuement, the food or the potion (no subject; an id or <c>none</c>), or a prayer (a prayer id; <c>on</c> or
/// <c>off</c>). Short ids are taken to be in the base namespace.
/// </summary>
[JsonConverter(typeof(ChangeJson))]
public sealed partial record Change
{
    public const string None = "none";
    public const string On = "on";
    public const string Off = "off";
    public const string Done = "done";
    public const string Open = "open";
    public const int LowestLevel = 1;
    private const int HighestLevel = 999;
    public const int HighestCount = 999;
    public const int HighestRank = 9;

    public Change(ChangeKind kind, string? subject, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Kind = kind;
        Subject = HasSubject(kind)
            ? FullSubject(kind, subject ?? throw new ArgumentNullException(nameof(subject)))
            : null;
        Value = NormalValue(kind, value) ?? (IsId(kind) && value != None ? Ids.Full(value) : value);
    }

    public ChangeKind Kind { get; }
    public string? Subject { get; }
    public string Value { get; }

    public ChangeKey Key => new(Kind, Subject);

    /// <summary>The level asked for, for a level change.</summary>
    public int? Level => Kind == ChangeKind.Level ? int.Parse(Value, CultureInfo.InvariantCulture) : null;

    /// <summary>How many of a purchase, or the rank index of a guild, for a shop or guild change.</summary>
    public int? Number =>
        Kind is ChangeKind.Shop or ChangeKind.Guild ? int.Parse(Value, CultureInfo.InvariantCulture) : null;

    /// <summary>Whether a quest change completes the quest.</summary>
    public bool Completes => Kind == ChangeKind.Quest && Value == Done;

    /// <summary>Whether a prayer change turns the prayer on.</summary>
    public bool TurnsOn => Kind == ChangeKind.Prayer && Value == On;

    /// <summary>The item, for an equip, toolbelt, food or potion change that isn't to none.</summary>
    public string? Item => IsItem(Kind) && Value != None ? Value : null;

    /// <summary>
    /// The change as text, such as <c>level=Strength:60</c>, <c>equip=Weapon:Iron_Sword</c>, <c>food=none</c> or
    /// <c>prayer=Thick_Skin:on</c>, with ids shortened as <see cref="Ids.Brief"/> does. An equip or toolbelt change
    /// whose slot or item is in another namespace is written with both ids in full, so it always reads back the same.
    /// </summary>
    public string ShortForm
    {
        get
        {
            if (OwnShortForm() is { } own)
                return own;
            var name = KindName(Kind) + "=";
            if (Subject == null)
                return name + (Value == None ? None : Ids.Brief(Value)); // the stance too: never none
            var subject = Ids.Brief(Subject);
            var value = IsId(Kind) && Value != None ? Ids.Brief(Value) : Value;
            if (IsSlot(Kind) && Value != None && subject.Contains(':') != value.Contains(':'))
                (subject, value) = (Subject, Value);
            return name + subject + ":" + value;
        }
    }

    public override string ToString() => ShortForm;

    /// <summary>
    /// Reads <see cref="ShortForm"/> back, with short or full ids. Throws <see cref="FormatException"/>.
    /// </summary>
    public static Change Parse(string text) =>
        TryParse(text, out var change, out var error) ? change! : throw new FormatException(error);

    /// <summary>Reads a change, or says why it isn't one; only the form is checked.</summary>
    public static bool TryParse(string text, out Change? change, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        change = null;
        error = Problem(text, out var kind, out var subject, out var value);
        if (error != null)
            return false;
        change = new Change(kind, subject, value!);
        return true;
    }

    private static string? Problem(string text, out ChangeKind kind, out string? subject, out string? value)
    {
        kind = default;
        subject = value = null;
        var equals = text.IndexOf('=', StringComparison.Ordinal);
        if (equals < 0)
            return $"'{text}' isn't a change: write <kind>=…, the kind one of {KindNames}";
        var name = text[..equals];
        var rest = text[(equals + 1)..];
        if (!TryKind(name, out kind))
            return $"'{name}' isn't a kind of change: use one of {KindNames}";
        if (HasOwnForm(kind))
            return OwnProblem(kind, text, rest, out subject, out value);
        if (!HasSubject(kind))
        {
            var never = kind is ChangeKind.Stance or ChangeKind.Spell; // the game always has one
            if (rest.Length == 0)
                return never ? $"{name} needs an id: {name}=<{name}>" : $"{name} needs an id or none: {name}=<id>";
            if (never && rest == None)
                return $"a {name} can't be none: {name}=<{name}>";
            value = rest;
            return null;
        }
        var parts = rest.Split(':');
        if (parts.Any(p => p.Length == 0) || parts.Length < 2 || parts.Length > 4)
            return $"'{text}': write {Form(kind)}";
        if (IsSlot(kind) && parts.Length == 3 && parts[2] != None && parts[0] != Ids.Base)
            (subject, value) = (parts[0], parts[1] + ":" + parts[2]);
        else if (IsSlot(kind) && parts.Length == 4)
            (subject, value) = (parts[0] + ":" + parts[1], parts[2] + ":" + parts[3]);
        else
            (subject, value) = (string.Join(':', parts[..^1]), parts[^1]);
        switch (kind)
        {
            case ChangeKind.Level:
                return Whole(text, ref value, LowestLevel, HighestLevel, "a level");
            case ChangeKind.Shop:
                return Whole(text, ref value, 1, HighestCount, "a count");
            case ChangeKind.Guild:
                return Whole(text, ref value, 0, HighestRank, "a rank");
            case ChangeKind.Prayer when value is not (On or Off):
                return $"'{text}': a prayer is turned on or off";
            case ChangeKind.Quest when value is not (Done or Open):
                return $"'{text}': a quest is done or open";
            case ChangeKind.Star when value != On:
                return $"'{text}': a star can only be turned on";
            default:
                return null;
        }
    }

    // A whole number in range, rewritten without leading zeros so equal changes have one short form.
    private static string? Whole(string text, ref string? value, int lowest, int highest, string what)
    {
        if (
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < lowest
            || number > highest
        )
            return $"'{text}': {what} is a whole number from {lowest} to {highest}";
        value = number.ToString(CultureInfo.InvariantCulture);
        return null;
    }

    private static string Form(ChangeKind kind) =>
        kind switch
        {
            ChangeKind.Level => "level=<skill>:<level>",
            ChangeKind.Quest => "quest=<quest>:done or quest=<quest>:open",
            ChangeKind.Shop => "shop=<purchase>:<count>",
            ChangeKind.Guild => "guild=<skill>:<rank>",
            ChangeKind.Star => "star=<node>:on",
            ChangeKind.Equip => "equip=<slot>:<item> or equip=<slot>:none",
            ChangeKind.Toolbelt => "toolbelt=<slot>:<item> or toolbelt=<slot>:none",
            _ => "prayer=<prayer>:on or prayer=<prayer>:off",
        };

    public static string KindName(ChangeKind kind) =>
        kind switch
        {
            ChangeKind.SlayerTask => "task",
            ChangeKind.SlayerTaskFor => "taskfor",
            _ => kind.ToString().ToLowerInvariant(),
        };

    private static readonly string KindNames = string.Join(", ", Enum.GetValues<ChangeKind>().Select(KindName));

    private static bool TryKind(string name, out ChangeKind kind)
    {
        foreach (var each in Enum.GetValues<ChangeKind>())
        {
            if (KindName(each) == name)
            {
                kind = each;
                return true;
            }
        }
        kind = default;
        return false;
    }

    /// <summary>
    /// Whether the kind has a subject: all but the stance, the spell, the imbuement, the food, the potion and the
    /// bonfire, which are one each, and the side's slayer task choice.
    /// </summary>
    private static bool HasSubject(ChangeKind kind) =>
        kind
            is not (
                ChangeKind.Stance
                or ChangeKind.Spell
                or ChangeKind.Imbuement
                or ChangeKind.Food
                or ChangeKind.Potion
                or ChangeKind.SlayerTask
                or ChangeKind.Bonfire
            );

    // A slot that holds an item: its subject and value are both ids, each with a namespace of its own.
    private static bool IsSlot(ChangeKind kind) => kind is ChangeKind.Equip or ChangeKind.Toolbelt;

    private static bool IsItem(ChangeKind kind) => IsSlot(kind) || kind is ChangeKind.Food or ChangeKind.Potion;

    // Values that are game ids (or none), shortened and given their namespace like other ids.
    private static bool IsId(ChangeKind kind) =>
        IsItem(kind) || kind is ChangeKind.Stance or ChangeKind.Spell or ChangeKind.Imbuement;

    /// <summary>Compares two changes in canonical order: kind, then subject (ordinal).</summary>
    internal static int Compare(Change a, Change b)
    {
        var byKind = a.Kind.CompareTo(b.Kind);
        return byKind != 0 ? byKind : string.CompareOrdinal(a.Subject, b.Subject);
    }

    internal sealed class ChangeJson : JsonConverter<Change>
    {
        public override Change Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                return Parse(reader.GetString() ?? "");
            }
            catch (FormatException e)
            {
                throw new JsonException(e.Message, e);
            }
        }

        public override void Write(Utf8JsonWriter writer, Change value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ShortForm);
    }
}

/// <summary>
/// Changes in canonical order (kind, then subject), at most one per key. Immutable: <see cref="With"/> and
/// <see cref="Without"/> return new lists. Equal lists have equal <see cref="ShortForm"/> forms, whatever order the
/// changes were made in.
/// </summary>
[JsonConverter(typeof(ChangeListJson))]
public sealed class ChangeList : IReadOnlyList<Change>, IEquatable<ChangeList>
{
    private readonly Change[] _changes;

    private ChangeList(Change[] sorted)
    {
        _changes = sorted;
        ShortForm = string.Join(' ', sorted.Select(c => c.ShortForm));
    }

    public static readonly ChangeList Empty = new([]);

    /// <summary>The changes' short forms in canonical order, joined by spaces; "" for none.</summary>
    public string ShortForm { get; }

    public int Count => _changes.Length;
    public bool IsEmpty => _changes.Length == 0;
    public Change this[int index] => _changes[index];

    /// <summary>The list with <paramref name="change"/>, replacing any change with the same key.</summary>
    public ChangeList With(Change change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return Of(_changes.Where(c => c.Key != change.Key).Append(change));
    }

    /// <summary>The list without the change for <paramref name="key"/> (this list when it has none).</summary>
    public ChangeList Without(ChangeKey key) =>
        _changes.Any(c => c.Key == key) ? Of(_changes.Where(c => c.Key != key)) : this;

    /// <summary>The change for <paramref name="key"/>, or null.</summary>
    public Change? Find(ChangeKey key) => Array.Find(_changes, c => c.Key == key);

    /// <summary>
    /// The equipment change behind what <paramref name="slot"/> holds, from the slots' items
    /// <paramref name="before"/> and <paramref name="after"/> the changes: the slot's own change, or else the one
    /// kept under another slot that moved this slot's item in or out. A two-handed weapon fills two slots, so one
    /// change alters both, and putting either back has to undo it. Null when the slot didn't change.
    /// </summary>
    public Change? EquipChangeBehind(
        string slot,
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyDictionary<string, string?> after
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (Find(new ChangeKey(ChangeKind.Equip, slot)) is { } own)
            return own;
        var was = before.GetValueOrDefault(slot);
        var now = after.GetValueOrDefault(slot);
        if (was == now)
            return null;
        return Array.Find(
            _changes,
            c =>
                c.Kind == ChangeKind.Equip
                && ((was != null && before.GetValueOrDefault(c.Subject!) == was) || (now != null && c.Item == now))
        );
    }

    /// <summary>A list of these changes. Throws <see cref="ArgumentException"/> when two share a key.</summary>
    public static ChangeList Of(IEnumerable<Change> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var sorted = changes.ToArray();
        Array.Sort(sorted, Change.Compare);
        for (var i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].Key == sorted[i - 1].Key)
                throw new ArgumentException(
                    $"{sorted[i - 1].ShortForm} and {sorted[i].ShortForm} change the same thing"
                );
        }
        return sorted.Length == 0 ? Empty : new ChangeList(sorted);
    }

    /// <summary>
    /// Reads changes separated by spaces. Throws <see cref="FormatException"/> for a bad one or a repeated key.
    /// </summary>
    public static ChangeList Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var changes = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Change.Parse)
            .ToList();
        try
        {
            return Of(changes);
        }
        catch (ArgumentException e)
        {
            throw new FormatException(e.Message, e);
        }
    }

    public IEnumerator<Change> GetEnumerator() => ((IEnumerable<Change>)_changes).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ChangeList? other) => other is not null && ShortForm == other.ShortForm;

    public override bool Equals(object? obj) => Equals(obj as ChangeList);

    public override int GetHashCode() => ShortForm.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => ShortForm;

    internal sealed class ChangeListJson : JsonConverter<ChangeList>
    {
        public override ChangeList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                return Parse(reader.GetString() ?? "");
            }
            catch (FormatException e)
            {
                throw new JsonException(e.Message, e);
            }
        }

        public override void Write(Utf8JsonWriter writer, ChangeList value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ShortForm);
    }
}

/// <summary>What became of a change when it was tried in a copy.</summary>
public enum ChangeState
{
    Accepted,

    /// <summary>The setup already has it.</summary>
    NoChange,

    /// <summary>The game wouldn't apply it; see the reasons.</summary>
    Refused,

    /// <summary>Applied with "ignore requirements" on, though a requirement was unmet; the reasons name it.</summary>
    RequirementIgnored,

    /// <summary>What it names doesn't exist in the running build, so there is nothing to try.</summary>
    NotInBuild,
}

public static class ChangeStates
{
    /// <summary>The state as replies and <c>cs2 ui</c> write it.</summary>
    public static string Name(ChangeState state) =>
        state switch
        {
            ChangeState.Accepted => "accepted",
            ChangeState.NoChange => "no-change",
            ChangeState.Refused => "refused",
            ChangeState.RequirementIgnored => "requirement-ignored",
            _ => "not-in-build",
        };
}

/// <summary>
/// A change as tried in a copy: the value it changed from (a level, an item id or <c>none</c>, <c>on</c>/<c>off</c>;
/// null if it couldn't be read), what became of it, and why (the game's condition text or CS2's reason).
/// </summary>
public sealed record ChangeOutcome(Change Change, string? From, ChangeState State, List<Note> Reasons)
{
    /// <summary>Whether the change is part of the changed setup.</summary>
    [JsonIgnore]
    public bool Applies => State is ChangeState.Accepted or ChangeState.RequirementIgnored;

    /// <summary>Whether the change was left out of the changed setup, with a reason.</summary>
    [JsonIgnore]
    public bool LeftOut => State is ChangeState.Refused or ChangeState.NotInBuild;

    /// <summary>The changes that apply, as the list the changed setup runs with.</summary>
    public static ChangeList Applied(IEnumerable<ChangeOutcome> outcomes) =>
        ChangeList.Of(outcomes.Where(o => o.Applies).Select(o => o.Change));
}
