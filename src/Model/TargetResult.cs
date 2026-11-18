using System.Text.Json.Serialization;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Model;

public enum TargetKind
{
    Enemy,
    Dungeon,

    /// <summary>A Slayer master's tasks: its figures come from its parts, one per monster of its pool.</summary>
    Master,

    /// <summary>
    /// Targets a gear search scores together (an area's enemies, the pins): each of its parts a target of its own,
    /// each counted equally against current gear's rate on it. Only in searches, never a row.
    /// </summary>
    Set,
}

public enum TargetState
{
    Queued,
    Building,
    Fighting,
    Finished,
    Unfinished,
    Refused,
    Cancelled,
    Unsupported,
}

public enum FightOutcome
{
    Kill,
    Death,

    /// <summary>The fight the trial limit stopped.</summary>
    RanOver,
}

/// <summary>One target's result in a run.</summary>
public sealed class TargetResult
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public TargetKind Kind { get; set; }
    public TargetState State { get; set; }

    /// <summary>
    /// Why the target isn't finished: refusal reasons, the trial over its limit, cancelled, a missing dependency.
    /// </summary>
    public List<Note> Reasons { get; set; } = [];
    public int TrialsDone { get; set; }
    public double GameSeconds { get; set; }
    public double RealSeconds { get; set; }

    /// <summary>The trials in consecutive groups (at most 30): the source of every estimate.</summary>
    public List<Tally> Batches { get; set; } = [];
    public List<Note> Assumptions { get; set; } = [];
    public List<Note> Unsupported { get; set; } = [];

    /// <summary>
    /// What of this setup and target CS2 doesn't model, each with the part it belongs to; null when nothing is left
    /// out, as in runs saved before these were read.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
    public List<GapNote>? NotSimulated { get; set; }

    /// <summary>The per-fight records, kept for dungeons only.</summary>
    public List<FightRecord> Fights { get; set; } = [];

    /// <summary>
    /// The earlier run this result was simulated in and reused from; null when it was simulated here.
    /// </summary>
    public ReuseOrigin? ReusedFrom { get; set; }

    /// <summary>
    /// The run seed this target was simulated with, when it isn't its run's <see cref="Run.Seed"/>: the second
    /// side of a comparison fights each target with the seed the first side's result came from.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? RunSeed { get; set; }

    /// <summary>
    /// Whether the target stopped at the run's precision before its full trials (<see cref="TrialsDone"/> is then
    /// lower).
    /// </summary>
    public bool StoppedEarly { get; set; }

    /// <summary>
    /// A dungeon's number of stages, from its data when the target started; null for enemies and older runs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Stages { get; set; }

    /// <summary>The player's max HP when the target started, the scale risk is drawn on; null in older runs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxHp { get; set; }

    /// <summary>
    /// The target's rare drops as the game defines them, read when the target started; null for older runs and
    /// targets never fought. Game data rather than a result, so not part of the results hash.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DropChance>? Drops { get; set; }

    /// <summary>
    /// The set's results at a race's checks, in order, each as a fight of that many trials gives it: carried from the
    /// fight to the race, which takes them off; null everywhere else. A fight stopped at a check is still
    /// <see cref="TargetState.Fighting"/>, and its result is the last of these.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TargetResult>? AtChecks { get; set; }

    /// <summary>The trials that ran over the trial limit, taken together; null when none did, and in older runs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Stall? Stall { get; set; }

    /// <summary>The batches' trials that ran over the limit; 0 without any, and in older runs.</summary>
    [JsonIgnore]
    public int RanOver => Batches.Sum(b => b.RanOver ?? 0);

    /// <summary>
    /// A master's monsters, each fought on the master's task, keyed by its own target key; null for other targets and
    /// in older runs. A master's own <see cref="Batches"/> stay empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TargetResult>? Parts { get; set; }

    /// <summary>The enemies a master's setup blocks, as target keys; null for other targets and in older runs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Blocked { get; set; }

    /// <summary>
    /// The saved loadout each of a master's monsters given one was fought with: its target key to the loadout slot;
    /// null when none was, for other targets and in older runs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Loadouts { get; set; }

    /// <summary>
    /// For a single monster fought on a slayer task by the setup's choice: the master whose task it was; null
    /// otherwise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Task { get; set; }

    /// <summary>
    /// For a single monster the setup put on a slayer task that no master gives: why it was fought off task; null
    /// otherwise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Note? TaskNote { get; set; }

    /// <summary>
    /// For a set of targets, the weight of each part, in order: one over current gear's rate on it in the metric
    /// raced, 0 where current gear has none. Null until current gear's result is known, and for other targets.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<double>? Weights { get; set; }

    /// <summary>A master's block cost and slots, read when the run started; null for other targets.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MasterFacts? Master { get; set; }

    public bool IsFinal => State >= TargetState.Finished;

    /// <summary>Whether the result has estimates: only finished results with batches do.</summary>
    public bool HasEstimates => State == TargetState.Finished && Batches.Count > 0;

    /// <summary>
    /// Whether the result has figures: its own estimates, or for a master its monsters', none of them stuck.
    /// </summary>
    [JsonIgnore]
    public bool HasFigures =>
        Kind switch
        {
            TargetKind.Master => State == TargetState.Finished
                && MasterParts.Counted(this) is { Count: > 0 } parts
                && MasterFigures.Stuck(parts) == null,
            TargetKind.Set => State == TargetState.Finished
                && Parts is { } members
                && members.Exists(m => m.HasFigures),
            _ => HasEstimates,
        };

    /// <summary>Whether the result holds what the game reported in its fights: runs saved before it was kept don't.</summary>
    [JsonIgnore]
    public bool HasFightCounts =>
        Kind == TargetKind.Master
            ? MasterParts.Counted(this) is { Count: > 0 } parts && parts.TrueForAll(p => p.HasFightCounts)
            : Batches.Count > 0 && Batches[0].Fight != null;

    /// <summary>
    /// Whether every trial that ended died. Such a result's values were earned while dying, so it is never ranked or best.
    /// Taken from the counts, because a death rate rounded for display reads 100% a fight or two short of it.
    /// False without estimates. Derived, so it stays out of the run file.
    /// </summary>
    [JsonIgnore]
    public bool NoneSurvived
    {
        get
        {
            if (!HasEstimates)
                return false;
            // A trial that ran over is neither kill nor death: every fight that ended, ended in a death.
            var ended = Batches.Sum(b => b.Trials) - RanOver;
            return ended > 0 && Batches.Sum(b => b.Deaths) == ended;
        }
    }

    /// <summary>The result's rule violations: a final state other than finished needs at least one reason.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (IsFinal && State != TargetState.Finished && Reasons.Count == 0)
            problems.Add($"{Key}: {State} without a reason");
        return problems;
    }
}

/// <summary>The sums over a batch's trials, keyed by game ids.</summary>
public sealed class Tally
{
    public int Trials { get; set; }

    /// <summary>Enemy kills, or dungeon completions.</summary>
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public double GameSeconds { get; set; }
    public SortedDictionary<string, double> Xp { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> Gained { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> Currency { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// What was used up, keyed <c>item:&lt;id&gt;</c>, <c>ammo:&lt;id&gt;</c>, <c>prayer</c> or
    /// <c>charges:&lt;id&gt;</c>.
    /// </summary>
    public SortedDictionary<string, long> Used { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> RareEvents { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The currency the items dropped would give if buried, by currency id (prayer points from bones): combat never
    /// buries them, so it is what they could give. Null when nothing dropped could be buried, and in runs saved
    /// before it was kept.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SortedDictionary<string, long>? Buryable { get; set; }

    /// <summary>
    /// The largest hit taken, from any cause, at its full size before it is cut to the HP left; null when not
    /// recorded (runs saved before it was kept).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxHit { get; set; }

    /// <summary>
    /// The smallest HP right after a hit, before anything heals, in trials the player survived (a survived trial
    /// with no hit counts its HP at the end); null when no trial survived, or when not recorded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MinHp { get; set; }

    /// <summary>The largest damage reflected back at the player; null when not recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxReflect { get; set; }

    /// <summary>
    /// The survived trials whose own highest hit was at least their own lowest HP; null when not recorded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CloseCalls { get; set; }

    /// <summary>What the game reported in the batch's fights; null when not recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FightCounts? Fight { get; set; }

    /// <summary>
    /// Trials of the batch that passed the trial limit, each neither a kill nor a death; null when not recorded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RanOver { get; set; }

    public void Add(Tally other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Trials += other.Trials;
        Kills += other.Kills;
        Deaths += other.Deaths;
        GameSeconds += other.GameSeconds;
        Merge(Xp, other.Xp, (a, b) => a + b);
        Merge(Gained, other.Gained, (a, b) => a + b);
        Merge(Currency, other.Currency, (a, b) => a + b);
        Merge(Used, other.Used, (a, b) => a + b);
        Merge(RareEvents, other.RareEvents, (a, b) => a + b);
        if (other.Buryable is { } buryable)
            Merge(Buryable ??= new(StringComparer.Ordinal), buryable, (a, b) => a + b);
        MaxHit = Either(MaxHit, other.MaxHit, Math.Max);
        MaxReflect = Either(MaxReflect, other.MaxReflect, Math.Max);
        MinHp = Either(MinHp, other.MinHp, Math.Min);
        CloseCalls = Either(CloseCalls, other.CloseCalls, (a, b) => a + b);
        RanOver = Either(RanOver, other.RanOver, (a, b) => a + b);
        if (other.Fight is { } fight)
            (Fight ??= new()).Add(fight);
    }

    public void Clear()
    {
        Trials = Kills = Deaths = 0;
        GameSeconds = 0;
        Xp.Clear();
        Gained.Clear();
        Currency.Clear();
        Used.Clear();
        RareEvents.Clear();
        Buryable = null;
        MaxHit = MinHp = MaxReflect = null;
        CloseCalls = null;
        Fight = null;
        RanOver = null;
    }

    // Both sides combined; a missing side leaves the other as it is.
    private static T? Either<T>(T? a, T? b, Func<T, T, T> combine)
        where T : struct => a is { } x ? (b is { } y ? combine(x, y) : x) : b;

    private static void Merge<T>(SortedDictionary<string, T> into, SortedDictionary<string, T> from, Func<T, T, T> add)
        where T : struct
    {
        foreach (var (key, value) in from)
            into[key] = into.TryGetValue(key, out var existing) ? add(existing, value) : value;
    }
}

/// <summary>One fight in a dungeon trial.</summary>
public sealed class FightRecord
{
    public int Trial { get; set; }
    public int Stage { get; set; }

    /// <summary>The enemy the game rolled for the stage.</summary>
    public string Enemy { get; set; } = "";
    public double GameSeconds { get; set; }

    /// <summary>HP the fight took, from every cause (attacks, effects, reflect); attacks only in older runs.</summary>
    public long DamageTaken { get; set; }
    public FightOutcome Outcome { get; set; }

    /// <summary>The fight's largest hit taken, at its full size; null in older runs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxHit { get; set; }

    /// <summary>
    /// The fight's lowest HP right after a hit, 0 when the player died; null with no hit, or in older runs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MinHp { get; set; }

    /// <summary>The player's attacks in this fight; null in older runs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Attacks { get; set; }

    /// <summary>The player's attacks that landed; null in older runs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Hits { get; set; }

    /// <summary>The enemy's health the player removed, from every cause; null in older runs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DamageDealt { get; set; }

    /// <summary>The enemy's attacks; null in older runs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? EnemyAttacks { get; set; }

    /// <summary>The enemy's attacks that landed; null in older runs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? EnemyHits { get; set; }
}

/// <summary>
/// One thing a result didn't simulate: the part it belongs to (<c>item:</c>, <c>prayer:</c>, <c>spell:</c>,
/// <c>imbuement:</c>, <c>food:</c> or <c>toolbelt:</c> and its id), its name, and what leaving it out means.
/// </summary>
public sealed record GapNote(string Owner, string Name, Note What)
{
    /// <summary>Each note once, over a master's or a set's parts; null when none has any.</summary>
    public static List<GapNote>? Merged(IEnumerable<TargetResult> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        List<GapNote> all =
        [
            .. parts.SelectMany(p => p.NotSimulated ?? []).DistinctBy(g => (g.Owner, g.What.English())),
        ];
        return all.Count == 0 ? null : all;
    }
}
