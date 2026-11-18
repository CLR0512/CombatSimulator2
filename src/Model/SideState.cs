using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>The two sides of the panel.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SideLetter>))]
public enum SideLetter
{
    A,
    B,
}

/// <summary>What a side starts from, before its changes.</summary>
public enum SideBaseKind
{
    /// <summary>The character as it is now.</summary>
    Current,
    Loadout,

    /// <summary>A saved run: its results only, since a run keeps no copy of the character.</summary>
    SavedRun,
}

/// <summary>
/// A side's base: the character as it is now, one of its loadouts by slot id, or a saved run by id. Written as
/// <c>current</c>, the slot id, or <c>run:&lt;id&gt;</c>.
/// </summary>
[JsonConverter(typeof(SideBaseJson))]
public sealed record SideBase
{
    private const string RunPrefix = "run:";

    private SideBase(SideBaseKind kind, string? id)
    {
        Kind = kind;
        Id = id;
    }

    public SideBaseKind Kind { get; }

    /// <summary>The loadout's slot id or the run's id; null for the character as it is now.</summary>
    public string? Id { get; }

    public static readonly SideBase Current = new(SideBaseKind.Current, null);

    public static SideBase Loadout(string slotId) =>
        string.IsNullOrEmpty(slotId) || slotId == Setup.Current
            ? throw new ArgumentException("a loadout base needs a slot id", nameof(slotId))
            : new(SideBaseKind.Loadout, slotId);

    public static SideBase SavedRun(string runId) =>
        string.IsNullOrEmpty(runId)
            ? throw new ArgumentException("a saved-run base needs a run id", nameof(runId))
            : new(SideBaseKind.SavedRun, runId);

    public string Text =>
        Kind switch
        {
            SideBaseKind.Current => Setup.Current,
            SideBaseKind.Loadout => Id!,
            _ => RunPrefix + Id,
        };

    /// <summary>Reads <see cref="Text"/> back; empty text is the character as it is now.</summary>
    public static SideBase Parse(string? text) =>
        string.IsNullOrEmpty(text) || text == Setup.Current ? Current
        : text.StartsWith(RunPrefix, StringComparison.Ordinal) && text.Length > RunPrefix.Length
            ? SavedRun(text[RunPrefix.Length..])
        : Loadout(text);

    public override string ToString() => Text;

    internal sealed class SideBaseJson : JsonConverter<SideBase>
    {
        public override SideBase Read(
            ref System.Text.Json.Utf8JsonReader reader,
            Type typeToConvert,
            System.Text.Json.JsonSerializerOptions options
        ) => Parse(reader.GetString());

        public override void Write(
            System.Text.Json.Utf8JsonWriter writer,
            SideBase value,
            System.Text.Json.JsonSerializerOptions options
        ) => writer.WriteStringValue(value.Text);
    }
}

/// <summary>
/// One side as the panel holds it: what it starts from, the changes tried on it, and whether its requirements are
/// ignored. Immutable; two sides are equal when their base, their changes and the flag are.
/// </summary>
public sealed record SideState
{
    [JsonConstructor]
    public SideState(SideBase @base, ChangeList? changes = null, bool ignoreRequirements = false)
    {
        ArgumentNullException.ThrowIfNull(@base);
        changes ??= ChangeList.Empty;
        if (@base.Kind == SideBaseKind.SavedRun && !changes.IsEmpty)
            throw new ArgumentException("a saved run's setup can't be rebuilt, so it carries no changes");
        Base = @base;
        Changes = changes;
        IgnoreRequirements = ignoreRequirements;
    }

    public SideBase Base { get; }
    public ChangeList Changes { get; }
    public bool IgnoreRequirements { get; }

    /// <summary>The character as it is now, with nothing changed.</summary>
    public static readonly SideState AsNow = new(SideBase.Current);

    /// <summary>Whether the side can be simulated and changed: a saved run only shows its stored results.</summary>
    [JsonIgnore]
    public bool CanChange => Base.Kind != SideBaseKind.SavedRun;

    /// <summary>The side on another base; changes are dropped when the base is a saved run.</summary>
    public SideState WithBase(SideBase @base) =>
        new(@base, @base.Kind == SideBaseKind.SavedRun ? ChangeList.Empty : Changes, IgnoreRequirements);

    /// <summary>
    /// The side when the player picks <paramref name="base"/> for it: a different start comes without the changes,
    /// which were made on the old one and kept would cover what was picked.
    /// </summary>
    public SideState StartingFrom(SideBase @base) =>
        Base.Equals(@base) ? this : new(@base, ChangeList.Empty, IgnoreRequirements);

    public SideState WithChanges(ChangeList changes) => new(Base, changes, IgnoreRequirements);

    public SideState WithIgnoreRequirements(bool ignore) => new(Base, Changes, ignore);

    /// <summary>
    /// The setup a run of this side uses. Throws for a saved run, which is never simulated again.
    /// </summary>
    public Setup ToSetup(string character, string? loadoutName = null)
    {
        if (!CanChange)
            throw new InvalidOperationException("a saved run is shown from its results, not simulated");
        return new Setup
        {
            Character = character,
            Loadout = Base.Text,
            LoadoutName = Base.Kind == SideBaseKind.Loadout ? loadoutName : null,
            IgnoreRequirements = IgnoreRequirements,
            Changes = Changes,
        };
    }

    /// <summary>The two sides exchanged whole.</summary>
    public static (SideState A, SideState B) Swap(SideState a, SideState b) => (b, a);
}
