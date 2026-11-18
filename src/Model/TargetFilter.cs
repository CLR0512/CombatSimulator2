using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>What a target is, as the filter's toggles name it. A monster in a slayer area has two.</summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<TargetTags>))]
public enum TargetTags
{
    None = 0,
    Monsters = 1,
    Dungeons = 2,
    Slayer = 4,

    /// <summary>A Slayer master's tasks; the Slayer tag keeps meaning a monster in a Slayer zone.</summary>
    Masters = 8,
    All = Monsters | Dungeons | Slayer | Masters,
}

/// <summary>
/// Which targets are shown and run: those matching the search (session only, never saved), that the setup can
/// fight or every target, with at least one of the tags that are on, in the chosen areas (null: every area).
/// </summary>
public sealed record TargetFilter
{
    [JsonIgnore]
    public string Search { get; init; } = "";

    public bool CanFightOnly { get; init; } = true;

    public TargetTags Kinds { get; init; } = TargetTags.All;

    public IReadOnlyList<string>? Areas { get; init; }

    public bool Equals(TargetFilter? other) =>
        other is not null
        && Search == other.Search
        && CanFightOnly == other.CanFightOnly
        && Kinds == other.Kinds
        && (Areas ?? []).SequenceEqual(other.Areas ?? [], StringComparer.Ordinal)
        && Areas is null == other.Areas is null;

    public override int GetHashCode() => HashCode.Combine(Search, CanFightOnly, Kinds, Areas?.Count);
}

/// <summary>
/// What the filter needs to know of a target: its key, the names searched, its area, its tags, whether the setup
/// can fight it (its requirements met or ignored).
/// </summary>
public sealed record TargetFact(string Key, string Name, string Area, string AreaName, TargetTags Tags, bool CanFight);
