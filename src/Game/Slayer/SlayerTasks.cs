using GamesByMalcs.Melvor2.Components.SlayerTasks;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Entities.Slayer;
using GamesByMalcs.Melvor2.Entities.Slayer.Statistics;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Slayer;

/// <summary>
/// A master's tasks as a run needs them: every monster it can give with its area (the blocked ones too), what it
/// blocks, what blocking costs and which slots are open. <see cref="Unlocked"/> and <see cref="Reasons"/> are its unlock as the
/// list reads any target's.
/// </summary>
internal sealed record MasterSpec(
    string Master,
    string Name,
    IReadOnlyList<TargetSpec> Pool,
    IReadOnlyList<string> Blocked,
    MasterFacts Facts,
    bool Unlocked,
    IReadOnlyList<Note> Reasons
);

/// <summary>
/// The game's Slayer tasks in a copy: forcing a master's task on one monster, rebuilding a master's pool the way the
/// game builds it, and reading a master for a run. The game has no call that sets a task, so the fields are written as
/// <see cref="Rereads"/> already names them.
/// </summary>
internal static class SlayerTasks
{
    // A task no fight finishes, so no next task is drawn mid-run.
    private const int UnreachableLength = 1_000_000_000;

    public static SlayerTasksComponent? Component(MelvorGame game) =>
        game.Entities.GetAllObjects().OfType<SlayerEntity>().FirstOrDefault()?.Tasks;

    public static ActiveSlayerMaster? Master(MelvorGame game, string masterId) =>
        Component(game)?.ActiveMasters.FirstOrDefault(p => p.Key.Id == masterId).Value;

    /// <summary>The save's current task per master, with what extending and rerolling it cost now.</summary>
    public static IReadOnlyList<TaskNow> Current(MelvorGame game) =>
        Component(game) is { } slayer
            ?
            [
                .. slayer
                    .ActiveMasters.Where(p => p.Value.CurrentTask != null)
                    .Select(p => new TaskNow(
                        p.Key.Id,
                        p.Value.CurrentTask!.Id,
                        p.Value.TaskProgress,
                        p.Value.TaskLength,
                        p.Value.IsTaskExtended,
                        p.Key.BaseTaskLength,
                        (long)p.Value.Stats.RerollCost.Value.Quantity,
                        (long)p.Value.Stats.ExtensionCost.Value.Quantity,
                        p.Value.Stats.RerollCost.Value.Currency.Id
                    )),
            ]
            : [];

    /// <summary>
    /// Every master's task cleared, then the master's set on the target's enemy with a length no fight reaches: what a
    /// master's monster is fought on. The copy's reset puts the save's tasks back.
    /// </summary>
    public static void Force(MelvorGame game, TargetSpec target, string masterId)
    {
        using var scope = Guards.Enter();
        var master =
            Master(game, masterId) ?? throw new InvalidOperationException($"no Slayer master {masterId} in the copy");
        var slayer = ClearAll(game);
        master._currentTask = game.Data.GetObject<Enemy>(target.Enemy!);
        master._taskLength = UnreachableLength;
        Rereads.TasksByEnemy(slayer);
    }

    /// <summary>
    /// The save's tasks kept, each with a length no fight reaches: what a target fought as the save has it is fought
    /// with. Trials follow one another, so a task ending after the kills it has left would turn every later trial off
    /// task, and the rates would fall with the number of trials. The copy's reset puts the save's lengths back.
    /// </summary>
    public static void KeepForRun(MelvorGame game)
    {
        using var scope = Guards.Enter();
        if (Component(game) is not { } slayer)
            return;
        foreach (var master in slayer.ActiveMasters.Values)
        {
            if (master.CurrentTask != null)
                master._taskLength = UnreachableLength;
        }
    }

    /// <summary>Every master's task cleared: what a monster the setup puts off task is fought with.</summary>
    public static void Clear(MelvorGame game)
    {
        using var scope = Guards.Enter();
        Rereads.TasksByEnemy(ClearAll(game));
    }

    private static SlayerTasksComponent ClearAll(MelvorGame game)
    {
        var slayer = Component(game) ?? throw new InvalidOperationException("the game has no Slayer tasks");
        foreach (var other in slayer.ActiveMasters.Values)
        {
            other._currentTask = null;
            other._taskLength = 0;
            other._taskProgress = 0;
        }
        slayer._mastersWithTask.Clear();
        return slayer;
    }

    /// <summary>
    /// The master a single monster is put on a task by: the first in the game's order whose pool, after the setup's
    /// blocks, holds it and whose unlock the setup meets; else the first whose pool holds it; null when none does.
    /// </summary>
    public static string? MasterFor(MelvorGame game, TargetSpec target, ChangeList? changes)
    {
        if (Component(game) is not { } slayer || target.Enemy == null)
            return null;
        var givers = slayer
            .ActiveMasters.Keys.Select(m => Read(game, m.Id, changes))
            .Where(m => m != null && m.Pool.Any(p => p.Enemy == target.Enemy && !m.Blocked.Contains(p.TargetKey)))
            .ToList();
        return (givers.FirstOrDefault(m => m!.Unlocked) ?? givers.FirstOrDefault())?.Master;
    }

    /// <summary>
    /// The master's pool built again from its areas and blocks, as the game builds it when a save loads: every enemy
    /// of its areas in data order, minus blocked ones. Only that loop: the rest of the game's
    /// load step subscribes to unlocks again each time it runs.
    /// </summary>
    public static void RebuildPool(ActiveSlayerMaster master)
    {
        master.AvailableEnemies.Clear();
        foreach (var area in master.Data.Areas)
        {
            foreach (var enemy in area.Enemies)
            {
                if (!master.IsEnemyBlocked(enemy))
                    master.AvailableEnemies.Add(enemy);
            }
        }
    }

    /// <summary>
    /// A master's eligibility, and each of its monsters': the master's unlock alone. Auto Slayer moves the player into
    /// a monster's area whatever the player has unlocked.
    /// </summary>
    public static Eligibility MasterEligibility(MelvorGame game, string masterId)
    {
        if (Master(game, masterId) is not { } master)
            return new Eligibility([Note.Of("no Slayer master {0} in this game", Ids.Brief(masterId))]);
        return new Eligibility(Game.Targets.Unmet(master.Data.UnlockRequirements));
    }

    /// <summary>Every enemy the save blocks, with its master, in the game's order.</summary>
    public static IEnumerable<(string Master, string Enemy)> Blocks(MelvorGame game) =>
        !Dependencies.IsAvailable(Dependencies.SlayerMasters)
            ? []
            : Component(game)
                ?.ActiveMasters.SelectMany(p =>
                    p.Value.BlockSlots.Where(s => s.BlockedEnemy != null).Select(s => (p.Key.Id, s.BlockedEnemy!.Id))
                )
                ?? [];

    /// <summary>Every master's target, in the game's order.</summary>
    public static IEnumerable<TargetSpec> Targets(MelvorGame game) =>
        Component(game)?.ActiveMasters.Keys.Select(m => new TargetSpec(Ids.MasterKey(m.Id), null)) ?? [];

    /// <summary>
    /// The master as a run of <paramref name="changes"/> fights it: its pool and blocks the save's, or those of the
    /// setup's blocks change for it, which a copy of the setup holds; null when the game has no such master.
    /// </summary>
    public static MasterSpec? Read(MelvorGame game, string masterId, ChangeList? changes)
    {
        if (Master(game, masterId) is not { } master)
            return null;
        var data = master.Data;
        var change = changes?.FirstOrDefault(c => c.Kind == ChangeKind.Blocks && c.Subject == masterId);
        var blockedEnemies =
            change != null
                ? data.Areas.SelectMany(a => a.Enemies).Where(e => change.Blocked.Contains(e.Id)).Distinct().ToList()
                : [.. master.BlockSlots.Where(s => s.BlockedEnemy != null).Select(s => s.BlockedEnemy!)];
        // The pool step's own rule over the master's areas, the blocked enemies kept: they are fought too, left out of
        // the master's figures, so a block suggestion can weigh unblocking them.
        var pool = data
            .Areas.SelectMany(a => a.Enemies.Select(e => (Area: a, Enemy: e)))
            .DistinctBy(p => p.Enemy)
            .Select(p => new TargetSpec(p.Area.Id, p.Enemy.Id, masterId))
            .ToList();
        var blocked = blockedEnemies.Select(e => new TargetSpec(master.GetCurrentTaskArea(e).Id, e.Id).Key).ToList();
        var facts = new MasterFacts
        {
            BlockCost = (long)master.Stats.BlockCost.Value.Quantity,
            Currency = master.Stats.BlockCost.Value.Currency.Id,
            Slots =
            [
                .. master.BlockSlots.Select(s => new BlockSlotFacts
                {
                    Id = s.Data.Id,
                    Open = s.IsUnlocked,
                    TasksNeeded = s.IsUnlocked ? 0 : TasksNeeded(s),
                }),
            ],
        };
        var reasons = Game.Targets.Unmet(data.UnlockRequirements);
        return new MasterSpec(masterId, data.DisplayName.Text, pool, blocked, facts, reasons.Count == 0, reasons);
    }

    // The tasks with the master still to complete before the slot opens, from its unmet task-count conditions.
    public static int TasksNeeded(ActiveEnemyBlockSlot slot)
    {
        var needed = 0;
        foreach (var condition in slot.Data.UnlockRequirements.Conditions)
        {
            if (
                condition is SlayerMasterStatisticCondition { Statistic: TasksCompletedStatistic } tasks
                && !tasks.IsMet
            )
                needed = Math.Max(needed, (int)Math.Ceiling(tasks.Value - tasks.Statistic.GetValue(tasks.Master)));
        }
        return needed;
    }
}
