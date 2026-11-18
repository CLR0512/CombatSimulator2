using System.Text.RegularExpressions;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// One enemy in one combat area, one whole dungeon, or one Slayer master's tasks (its key in <see cref="Area"/>), by
/// id. <see cref="Key"/> keys its result. <see cref="Task"/> is the master an enemy is fought for, on that master's
/// task, as part of the master's result; null for every other target. A part's key starts with its master's.
/// <see cref="SingleTask"/> is a single monster's slayer task as the setup chose it: a master's id (fought on that
/// master's task), <see cref="OffTask"/> or <see cref="NoMaster"/> (fought with no task); null as the save has it.
/// </summary>
internal sealed record TargetSpec(string Area, string? Enemy, string? Task = null, string? SingleTask = null)
{
    public const string OffTask = "";

    /// <summary>Put on a task by the setup, but no master gives it: fought with no task, and its result says so.</summary>
    public const string NoMaster = "-";

    /// <summary>The single monster's master when the setup puts it on a task; null otherwise.</summary>
    public string? SingleMaster => SingleTask is { Length: > 1 } master ? master : null;

    /// <summary>
    /// The key the copy is reseeded with: a monster on a master's task by the setup's choice is seeded as that master's
    /// part, so the two give the same figures.
    /// </summary>
    public string SeedKey => SingleMaster is { } master ? Ids.PartKey(master, TargetKey) : Key;

    public TargetKind Kind =>
        Enemy != null ? TargetKind.Enemy
        : Ids.IsMaster(Area) ? TargetKind.Master
        : Ids.IsSet(Area) ? TargetKind.Set
        : TargetKind.Dungeon;

    public string Key => Task == null ? TargetKey : Ids.PartKey(Task, TargetKey);

    /// <summary>The key of what is fought, without the master a part is fought for.</summary>
    public string TargetKey => Enemy == null ? Area : Area + "/" + Enemy;

    /// <summary>The master's id, for a master's target.</summary>
    public string? Master => Kind == TargetKind.Master ? Ids.MasterOf(Area) : null;

    public static TargetSpec FromKey(string key)
    {
        if (Ids.IsMaster(key) || Ids.IsSet(key))
            return new(key, null);
        var task = Ids.IsPart(key) ? Ids.MasterOf(key) : null;
        var target = task == null ? key : Ids.PartTarget(key);
        var slash = target.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? new(target, null, task) : new(target[..slash], target[(slash + 1)..], task);
    }
}

/// <summary>A target's eligibility in one copy: every unmet condition's description.</summary>
internal sealed record Eligibility(IReadOnlyList<Note> Reasons)
{
    public bool Eligible => Reasons.Count == 0;
}

/// <summary>Targets from the game's data, and their eligibility from the game's own conditions.</summary>
internal static partial class Targets
{
    /// <summary>
    /// Every area/enemy pair and dungeon in the game's combat areas, in the game's order, then every Slayer master
    /// when the build has what masters need. Masters come last, so saved lines naming targets by place still read.
    /// </summary>
    public static IReadOnlyList<TargetSpec> All(MelvorGame game)
    {
        var targets = new List<TargetSpec>();
        foreach (var area in LiveGame.Combat(game).Areas.Config.Areas)
        {
            if (area is CombatArea regular)
                targets.AddRange(regular.Enemies.Select(enemy => new TargetSpec(regular.Id, enemy.Id)));
            else if (area is Dungeon dungeon)
                targets.Add(new TargetSpec(dungeon.Id, null));
        }
        if (Dependencies.IsAvailable(Dependencies.SlayerMasters))
            targets.AddRange(Slayer.SlayerTasks.Targets(game));
        return targets;
    }

    /// <summary>
    /// Parses <c>&lt;area&gt;/&lt;enemy&gt;</c>, <c>&lt;dungeon&gt;</c> or <c>master:&lt;master&gt;</c>, short ids
    /// allowed; with <paramref name="sets"/>, also <c>area:&lt;area&gt;</c>.
    /// </summary>
    public static TargetSpec Resolve(MelvorGame game, string text, bool sets = false)
    {
        if (sets && text.StartsWith(Ids.AreaPrefix, StringComparison.Ordinal))
        {
            var areaId = Ids.Full(text[Ids.AreaPrefix.Length..]);
            return game.Data.TryGetObject<CombatArea>(areaId, out _)
                ? new TargetSpec(Ids.AreaPrefix + areaId, null)
                : throw new CommandException($"no combat area '{text[Ids.AreaPrefix.Length..]}'");
        }
        var spec = TargetSpec.FromKey(Ids.FullKey(text));
        if (spec.Task != null)
            throw new CommandException($"'{text}' is a master's monster: give master:<master>");
        if (spec.Master is { } master)
            return Slayer.SlayerTasks.Master(game, master) != null
                ? spec
                : throw new CommandException($"no Slayer master '{Ids.Brief(master)}'");
        if (!game.Data.TryGetObject<BaseCombatArea>(spec.Area, out var area))
            throw new CommandException($"unknown target '{text}'");
        if (spec.Enemy == null)
            return area is Dungeon ? spec : throw new CommandException($"'{text}' is an area: give <area>/<enemy>");
        if (area is not CombatArea combatArea)
            throw new CommandException($"'{Ids.Brief(spec.Area)}' is a dungeon: give it without an enemy");
        if (!game.Data.TryGetObject<Enemy>(spec.Enemy, out var enemy) || !combatArea.Enemies.Contains(enemy))
            throw new CommandException($"{Ids.Brief(spec.Area)} has no enemy '{Ids.Brief(spec.Enemy)}'");
        return spec;
    }

    public static BaseCombatArea Area(MelvorGame game, TargetSpec target) =>
        game.Data.GetObject<BaseCombatArea>(target.Area);

    /// <summary>The targets a set stands for: an area's enemies in the game's order, or the pins as given.</summary>
    public static IReadOnlyList<TargetSpec> Members(MelvorGame game, TargetSpec set)
    {
        if (set.Area.StartsWith(Ids.AreaPrefix, StringComparison.Ordinal))
            return game.Data.TryGetObject<CombatArea>(set.Area[Ids.AreaPrefix.Length..], out var area)
                ? [.. area.Enemies.Select(e => new TargetSpec(area.Id, e.Id))]
                : [];
        return [.. set.Area[Ids.PinnedPrefix.Length..].Split(',').Select(TargetSpec.FromKey)];
    }

    /// <summary>
    /// The target's eligibility in this game: every unmet condition of the area's unlock requirements and the
    /// enemy's requirements; for a master's monster, its master's.
    /// </summary>
    public static Eligibility Check(MelvorGame game, TargetSpec target)
    {
        if ((target.Task ?? target.Master) is { } master)
            return Slayer.SlayerTasks.MasterEligibility(game, master);
        // A set's targets are each judged when they are fought.
        if (target.Kind == TargetKind.Set)
            return new Eligibility([]);
        var reasons = new List<Note>();
        Unmet(Area(game, target).UnlockRequirements, reasons);
        if (target.Enemy != null)
            Unmet(game.Data.GetObject<Enemy>(target.Enemy).Requirements, reasons);
        return new Eligibility([.. reasons.DistinctBy(r => r.English(), StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Every unmet condition of <paramref name="group"/> in its game, described as the game describes it.
    /// </summary>
    public static List<Note> Unmet(ConditionGroup? group)
    {
        var reasons = new List<Note>();
        Unmet(group, reasons);
        return [.. reasons.DistinctBy(r => r.English(), StringComparer.Ordinal)];
    }

    private static void Unmet(ConditionGroup? group, List<Note> reasons)
    {
        if (group == null)
            return;
        foreach (var condition in group.Conditions)
        {
            if (!condition.IsMet)
                reasons.Add(Note.Text(Describe(condition)));
        }
    }

    internal static string Describe(Condition condition)
    {
        string text;
        try
        {
            text = Markup().Replace(condition.GetDescription(centered: false, coloured: false), "").Trim();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The condition's type name stands in for a description the game can't give.
            Log.WarnOnce($"{condition.GetType().Name} has no description", e);
            text = "";
        }
        return text.Length > 0 ? Whitespace().Replace(text, " ") : condition.GetType().Name;
    }

    /// <summary>The game's text without its markup (tags, icons, resource paths), on one line.</summary>
    public static string Plain(string text) => Whitespace().Replace(Markup().Replace(text, ""), " ").Trim();

    [GeneratedRegex(@"\[[^\]]*\]|res://\S+")]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>
/// A listed target: its group and name, its area and its area's category from the game's data, whether the area
/// is one of the game's slayer zones, and its eligibility for one setup.
/// </summary>
internal sealed record TargetInfo(
    TargetSpec Spec,
    string Group,
    string Name,
    string AreaName,
    string Category,
    bool Slayer,
    IReadOnlyList<Note> Reasons
)
{
    public string Key => Spec.Key;

    public TargetKind Kind => Spec.Kind;

    public string Area => Spec.Area;

    public bool Eligible => Reasons.Count == 0;

    /// <summary>
    /// What the target filter reads: the target can be fought when its requirements are met or
    /// <paramref name="ignoreRequirements"/> is on.
    /// </summary>
    public TargetFact Fact(bool ignoreRequirements) =>
        new(
            Key,
            Name,
            Area,
            AreaName,
            Kind switch
            {
                TargetKind.Dungeon => TargetTags.Dungeons,
                TargetKind.Master => TargetTags.Masters,
                _ => TargetTags.Monsters,
            } | (Slayer ? TargetTags.Slayer : TargetTags.None),
            Eligible || ignoreRequirements
        );
}

/// <summary>
/// Every target for one setup, with its eligibility. Names and groups are read from the live game on the main
/// thread; eligibility comes from a check copy built from a snapshot on a background thread (≈1 s) and prepared as
/// a run of the setup would be, its changes included; used only for checks.
/// </summary>
internal sealed class TargetCatalog
{
    private TargetCatalog(string setup, IReadOnlyList<TargetInfo> targets, SetupGear gear)
    {
        Setup = setup;
        List = targets;
        Gear = gear;
    }

    /// <summary>What the setup wears, read from the same check copy.</summary>
    public SetupGear Gear { get; }

    /// <summary>The setup identity it was checked for.</summary>
    public string Setup { get; }

    public IReadOnlyList<TargetInfo> List { get; }

    private Dictionary<string, TargetInfo>? _byKey;

    public TargetInfo? Find(string key)
    {
        _byKey ??= List.ToDictionary(t => t.Key, StringComparer.Ordinal);
        return _byKey.GetValueOrDefault(key);
    }

    /// <summary>
    /// Snapshots the live game on the main thread and starts the check; <paramref name="done"/> runs on the main
    /// thread with the catalog, or with the error.
    /// </summary>
    public static void Check(MelvorGame game, Setup setup, string? loadoutSlot, Action<TargetCatalog?, Exception?> done)
    {
        MainThread.Assert("TargetCatalog.Check");
        SpoilerGuard.Refresh(game);
        var specs = Targets.All(game);
        var labels = specs.ToDictionary(s => s.Key, s => Label(game, s));
        var snapshot = LiveGame.TakeSnapshot(game);
        var identity = setup.Identity;
        var changes = setup.Changes;
        var ignore = setup.IgnoreRequirements;
        var thread = new Thread(() =>
        {
            try
            {
                using var copy = Copy.Build();
                // With the setup's changes: a level or an item moves what it can fight.
                copy.Prepare(snapshot, loadoutSlot, changes, ignore, throwOnRefusal: false);
                var gear = copy.ReadGear();
                var infos = new List<TargetInfo>(specs.Count);
                foreach (var spec in specs)
                {
                    var eligibility = Targets.Check(copy.Game, spec);
                    var label = labels[spec.Key];
                    infos.Add(
                        new TargetInfo(
                            spec,
                            label.Group,
                            label.Name,
                            label.AreaName,
                            label.Category,
                            label.Slayer,
                            eligibility.Reasons
                        )
                    );
                }
                MainThread.Post(() => done(new TargetCatalog(identity, infos, gear), null));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                MainThread.Post(() => done(null, e));
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "cs2-check",
        };
        thread.Start();
    }

    // The group masters are listed under: the game names no category for them.
    private const string SlayerMastersGroup = "Slayer masters";

    /// <summary>The category the game puts its slayer zones in.</summary>
    public const string SlayerCategory = "MelvorBase:SlayerZones";

    // "Combat Areas / Golbin Forest" and "Golbin"; a dungeon's group is its category.
    private static (string Group, string Name, string AreaName, string Category, bool Slayer) Label(
        MelvorGame game,
        TargetSpec spec
    )
    {
        if (spec.Master != null)
        {
            var name = LiveGame.TargetName(game, spec);
            return (SlayerMastersGroup, name, name, SlayerMastersGroup, false);
        }
        var area = Area(game, spec);
        var category = area.Category.DisplayName.Text;
        var areaName = LiveGame.NameOf(game, area.Id);
        var slayer = area.Category.Id == SlayerCategory;
        return spec.Enemy == null
            ? (category, areaName, areaName, category, slayer)
            : ($"{category} / {areaName}", LiveGame.NameOf(game, spec.Enemy), areaName, category, slayer);
    }

    private static BaseCombatArea Area(MelvorGame game, TargetSpec spec) =>
        game.Data.GetObject<BaseCombatArea>(spec.Area);
}
