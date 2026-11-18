using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model.Search;

public enum AnswerState
{
    /// <summary>A set better than current gear by more than the margin.</summary>
    Found,

    /// <summary>Current gear is the best or as good as it.</summary>
    KeepCurrent,

    /// <summary>Every set is over the death limit.</summary>
    NoneWithinLimit,

    /// <summary>No set finished a fight.</summary>
    NoneKills,

    /// <summary>Only current gear is left to try.</summary>
    NothingToSearch,
    Cancelled,
    Failed,
}

/// <summary>
/// One slot that differs from current gear: the items' ids and the game's names, none as null, and the new item's
/// mark when the character doesn't have it.
/// </summary>
public sealed record SlotChange(
    string Slot,
    string SlotName,
    string? Old,
    string? OldName,
    string? New,
    string? NewName
)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Mark? Mark { get; init; }

    /// <summary>On an alternative as shown: the slot holds something else than the best's.</summary>
    [JsonIgnore]
    public bool DiffersFromBest { get; init; }
}

/// <summary>
/// One choice beyond gear in a set, against the setup's own: the kind (and toolbelt slot), the old and new options'
/// ids and names (none as empty), the new one's mark, and what the set uses of the supplies it brings.
/// </summary>
public sealed class ChoiceChange
{
    public ChoiceKind Kind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subject { get; set; }
    public string Name { get; set; } = "";
    public List<string> Old { get; set; } = [];
    public List<string> OldNames { get; set; } = [];
    public List<string> New { get; set; } = [];
    public List<string> NewNames { get; set; } = [];
    public Mark Mark { get; set; } = Mark.None;
    public List<SupplyCost> Supplies { get; set; } = [];

    /// <summary>On an alternative as shown: it holds something else than the best's.</summary>
    [JsonIgnore]
    public bool DiffersFromBest { get; set; }

    /// <summary>The set keeps the setup's own option.</summary>
    [JsonIgnore]
    public bool Keep => Old.Order(StringComparer.Ordinal).SequenceEqual(New.Order(StringComparer.Ordinal));
}

/// <summary>
/// A set with its result of the last stage. <see cref="Gain"/> is its difference from current gear in the goal's
/// metric and <see cref="Percent"/> the same relative to current gear, both paired on the seed with a 95% range;
/// <see cref="FromBest"/> is a tie's difference from the best. <see cref="Assumes"/> names items the set uses up
/// in fights, which are taken to last.
/// </summary>
public sealed class SetResult
{
    public ChangeList Changes { get; set; } = ChangeList.Empty;
    public List<SlotChange> Slots { get; set; } = [];

    /// <summary>The stance the set fights in; null when it is current gear's.</summary>
    public string? Stance { get; set; }
    public string Style { get; set; } = "";
    public TargetResult Result { get; set; } = new();

    /// <summary>The goal's metric as this set fought it: <c>xp:main</c> is the skill the set trains.</summary>
    public string Metric { get; set; } = "";

    /// <summary>Sets ruled equal to this one, which share its result.</summary>
    public List<ChangeList> Same { get; set; } = [];
    public Estimate? Gain { get; set; }
    public Estimate? Percent { get; set; }
    public Estimate? FromBest { get; set; }
    public List<string> Assumes { get; set; } = [];

    /// <summary>The choices beyond gear a setup search tried, one per kind (and toolbelt slot).</summary>
    public List<ChoiceChange> Choices { get; set; } = [];

    /// <summary>The supplies the set uses an hour, each in its own units.</summary>
    public List<SupplyCost> Supplies { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Estimate? DamageTaken { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Estimate? Food { get; set; }

    /// <summary>
    /// Whether the set holds something the character can't use yet: the search fought it with that requirement
    /// ignored, so a setup made of the set must ignore requirements to be the set that was found.
    /// </summary>
    [JsonIgnore]
    public bool NeedsRequirementsIgnored =>
        Slots.Any(s => s.Mark?.Kind == MarkKind.Locked) || Choices.Any(c => c.Mark.Kind == MarkKind.Locked);
}

/// <summary>
/// Sets beside the best, within its 95% range: one using fewer supplies, and one taking less damage. Each is absent
/// when the best already is it.
/// </summary>
public sealed class Alternatives
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SetResult? Cheaper { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SetResult? Safer { get; set; }
}

/// <summary>One style's best set and its ties, when a search covered several.</summary>
public sealed class StyleAnswer
{
    public string Style { get; set; } = "";
    public SetResult? Best { get; set; }
    public List<SetResult> Ties { get; set; } = [];
}

/// <summary>A style left out of a search, with why.</summary>
public sealed record SkippedStyle(string Style, Note Why);

/// <summary>A set or item that wasn't fought, with why.</summary>
public sealed record LeftOut(string What, Note Why);

/// <summary>One stage of a search: how many sets fought its trials, and how many it dropped and left.</summary>
public sealed record StageSummary(int Trials, int Fought, int Dropped, int Left, double Seconds);

/// <summary>An unowned item that is a gain, or safer at no cost, when swapped alone into current gear.</summary>
public sealed class DropGain
{
    public string Item { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slot { get; set; } = "";
    public string SlotName { get; set; } = "";

    /// <summary>What the swap takes off, by name.</summary>
    public List<string> Displaced { get; set; } = [];

    /// <summary>Where the item comes from, as the game's data names it.</summary>
    public Note Source { get; set; } = Answers.OtherSource;
    public ChangeList Changes { get; set; } = ChangeList.Empty;
    public TargetResult Result { get; set; } = new();

    /// <summary>The goal's metric as the swap fought it.</summary>
    public string Metric { get; set; } = "";
    public Estimate? Gain { get; set; }
    public Estimate? Percent { get; set; }

    /// <summary>Among the safer: the damage a second current gear takes, and the swap.</summary>
    public Estimate? TakenBefore { get; set; }
    public Estimate? Taken { get; set; }
}

/// <summary>An unowned item the character can't wear yet, with each requirement it is missing.</summary>
public sealed record NotYetWearable(string Item, string Name, Note Source, List<Note> Missing);

/// <summary>
/// What an answer was searched under. An answer is current only while every part still matches; a null
/// <see cref="Seed"/> in the key asked about matches any.
/// </summary>
public sealed record AnswerKey
{
    /// <summary>The character searched: each keeps its own answers, which another character never sees.</summary>
    public string Character { get; init; } = "";
    public string Setup { get; init; } = "";
    public string Gear { get; init; } = "";
    public string Levels { get; init; } = "";
    public string GameBuild { get; init; } = "";
    public string ModBuild { get; init; } = "";
    public Goal Goal { get; init; } = new(Metrics.MainXp);
    public List<string> Locks { get; init; } = [];
    public List<string> Excluded { get; init; } = [];
    public bool AllStyles { get; init; }
    public double Share { get; init; }
    public ulong? Seed { get; init; }

    /// <summary>The kinds of choice tried besides gear, by name; none for a gear search.</summary>
    public List<string> Kinds { get; init; } = [];
    public bool IncludeUnowned { get; init; }

    /// <summary>
    /// A hash of the food and toolbelt items owned and the prayers, spells and imbuements unlocked; only a search
    /// that tried other kinds goes out of date by it.
    /// </summary>
    public string Supplies { get; init; } = "";

    /// <summary>
    /// What differs between this key, an answer's, and <paramref name="now"/>, in the words an out-of-date answer
    /// is shown with; empty when the answer is current.
    /// </summary>
    public IReadOnlyList<Note> Changed(AnswerKey now)
    {
        ArgumentNullException.ThrowIfNull(now);
        var changed = new List<Note>();
        if (Setup != now.Setup)
            changed.Add(Note.Of("setup changed"));
        if (Gear != now.Gear)
            changed.Add(Note.Of("gear changed"));
        if (Levels != now.Levels)
            changed.Add(Note.Of("levels changed"));
        if (GameBuild != now.GameBuild)
            changed.Add(Note.Of("game updated"));
        if (ModBuild != now.ModBuild)
            changed.Add(Note.Of("Combat Simulator 2 updated"));
        if (Goal != now.Goal)
            changed.Add(Note.Of("goal changed"));
        if (!Locks.Order(StringComparer.Ordinal).SequenceEqual(now.Locks.Order(StringComparer.Ordinal)))
            changed.Add(Note.Of("locks changed"));
        if (!Excluded.Order(StringComparer.Ordinal).SequenceEqual(now.Excluded.Order(StringComparer.Ordinal)))
            changed.Add(Note.Of("excluded items changed"));
        if (AllStyles != now.AllStyles)
            changed.Add(Note.Of("styles changed"));
        if (Share != now.Share)
            changed.Add(Note.Of("margin changed"));
        if (now.Seed is { } seed && Seed != seed)
            changed.Add(Note.Of("seed changed"));
        if (!Kinds.SequenceEqual(now.Kinds) || IncludeUnowned != now.IncludeUnowned)
            changed.Add(Note.Of("choices tried changed"));
        else if (Kinds.Count > 0 && Supplies != now.Supplies)
            changed.Add(Note.Of("prayers, spells, food or toolbelt changed"));
        return changed;
    }
}

/// <summary>
/// What a search found. Every number in it is from fights: the results of current gear, the best and the ties (for
/// Droptimizer, of each gain) are kept; the other candidates' are not.
/// </summary>
public sealed class Answer
{
    public SearchKind Kind { get; set; }
    public AnswerKey Key { get; set; } = new();
    public string Target { get; set; } = "";
    public string TargetName { get; set; } = "";
    public AnswerState State { get; set; }

    /// <summary>The goal's metric as fought: <c>xp:main</c> resolved to current gear's skill.</summary>
    public string Metric { get; set; } = "";
    public SetResult? Current { get; set; }
    public SetResult? Best { get; set; }
    public List<SetResult> Ties { get; set; } = [];

    /// <summary>Per style, when the search covered more than the setup weapon's.</summary>
    public List<StyleAnswer> ByStyle { get; set; } = [];
    public List<SkippedStyle> SkippedStyles { get; set; } = [];

    /// <summary>The share of the best's value within which a set is as good.</summary>
    public double Margin { get; set; }

    /// <summary>Sets before any rule; beyond what a double counts exactly it is only shown rounded.</summary>
    public double Possible { get; set; }

    /// <summary>Sets ruled out without a fight, by rule.</summary>
    public Dictionary<string, double> RuledOut { get; set; } = [];
    public int Simulated { get; set; }
    public List<LeftOut> LeftOut { get; set; } = [];
    public List<StageSummary> Stages { get; set; } = [];

    /// <summary>Found a slot at a time: a better combination may exist.</summary>
    public bool Climbed { get; set; }

    /// <summary>The leaders were fought again; <see cref="Seed"/> is then the second seed.</summary>
    public bool Reran { get; set; }

    /// <summary>The stage the best was behind at, when <see cref="Reran"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BehindAtStage { get; set; }

    /// <summary>The seed the results shown were fought with.</summary>
    public ulong Seed { get; set; }

    /// <summary>With no set within the limit: the one with the fewest deaths.</summary>
    public SetResult? Closest { get; set; }

    /// <summary>With no set that kills: why current gear's fights didn't finish, as results word it.</summary>
    public Note? Reason { get; set; }

    /// <summary>
    /// For a search over several targets, those on which another set fought in it is better alone than the best,
    /// beyond both their ranges: each would want a set of its own. Null for one target.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? WantsOwnSet { get; set; }

    /// <summary>The stage a cancelled search stopped after.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CancelledAfter { get; set; }

    /// <summary>
    /// For a search stopped after a stage every set left had finished: that stage. The answer is then that stage's
    /// best so far, kept like a finished search's; null for a search that ran to its end.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StoppedAtStage { get; set; }

    /// <summary>Droptimizer: the unowned items read, those wearable now, and those another item beat.</summary>
    public int Unowned { get; set; }
    public int Wearable { get; set; }

    public List<DropGain> Gains { get; set; } = [];

    /// <summary>Droptimizer: items as good on the goal that take clearly less damage, the safest first.</summary>
    public List<DropGain> Safer { get; set; } = [];
    public int NoGain { get; set; }
    public int OverLimit { get; set; }
    public List<NotYetWearable> NotYetWearable { get; set; } = [];
    public DateTime FoundAt { get; set; }

    /// <summary>The loadout, or the character as is, the answer was found for.</summary>
    public string Loadout { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Alternatives? Alternatives { get; set; }

    /// <summary>Single-choice ties raced again in the finished setup.</summary>
    public List<CloseCall> CloseCalls { get; set; } = [];

    /// <summary>Choices left out because what they do in a fight can't be judged.</summary>
    public List<LeftOut> LeftOutUnjudged { get; set; } = [];

    /// <summary>Parts the best carries that aren't simulated, so its figures may be off by them.</summary>
    public List<Note> WinsThroughGap { get; set; } = [];

    /// <summary>A stopped setup search whose close calls weren't settled.</summary>
    public bool StoppedBeforeCloseCalls { get; set; }

    /// <summary>The best's gain over current gear; null without a best.</summary>
    [JsonIgnore]
    public Estimate? Gain => Best?.Gain;

    /// <summary>Whether the answer is one to keep: a search that ran to its end.</summary>
    [JsonIgnore]
    public bool Finished => State is not (AnswerState.Cancelled or AnswerState.Failed);
}
