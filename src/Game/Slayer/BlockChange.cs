using GamesByMalcs.Melvor2.Components.SlayerTasks;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game.Slayer;

/// <summary>
/// A master's blocks, set in a copy through the game's own block and unblock calls, then the pool rebuilt as the game
/// builds it. The game's block call pays and doesn't check that its slot is open: a block that needs a slot the save
/// hasn't opened is made in a slot opened in the copy and marked with the tasks still needed, and the coins blocks cost
/// are given first and the rest taken back after, so the wallet ends as it was. Copies only.
/// </summary>
internal static class BlockChange
{
    public static ChangeOutcome Apply(Copy copy, Change change)
    {
        if (SlayerTasks.Master(copy.Game, change.Subject!) is not { } master)
            return Changes.Missing(change, null);
        var from = Blocked(master);
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        var enemies = new List<Enemy>();
        foreach (var id in change.Blocked)
        {
            if (!copy.Game.Data.TryGetObject<Enemy>(id, out var enemy))
                return Changes.Missing(change, from);
            if (!master.Data.Areas.Any(a => a.Enemies.Contains(enemy)))
                return Changes.Refused(
                    change,
                    from,
                    Note.Of("{0} isn't one of {1}'s monsters", enemy.DisplayName.Text, master.Data.DisplayName.Text)
                );
            enemies.Add(enemy);
        }
        if (enemies.Count > master.BlockSlots.Count)
            return Changes.Refused(
                change,
                from,
                Note.Of("{0} has {1} block slots", master.Data.DisplayName.Text, master.BlockSlots.Count)
            );
        // The slots in order, as the game fills them: a block past the open ones needs the next slot opened.
        var reasons = new List<Note>();
        for (var i = 0; i < enemies.Count; i++)
        {
            var slot = master.BlockSlots[i];
            if (slot.IsUnlocked)
                continue;
            reasons.Add(NeedsSlot(i + 1, SlayerTasks.TasksNeeded(slot)));
            slot._isUnlocked = true;
        }
        foreach (var slot in master.BlockSlots.Where(s => s.BlockedEnemy != null))
            master.UnblockEnemy(slot);
        var cost = master.Stats.BlockCost.Value;
        var before = master.Wallet.GetQuantity(cost.Currency);
        master.Wallet.AddQuantity(cost.Currency, cost.Quantity * enemies.Count);
        for (var i = 0; i < enemies.Count; i++)
            master.BlockEnemy(enemies[i], master.BlockSlots[i]);
        var left = master.Wallet.GetQuantity(cost.Currency) - before;
        if (left > 0)
            master.Wallet.RemoveQuantity(cost.Currency, left);
        SlayerTasks.RebuildPool(master);
        if (Blocked(master) != change.Value)
            return Changes.Refused(change, from, Note.Of("the game blocked {0}", Blocked(master)));
        return reasons.Count > 0
            ? new(change, from, ChangeState.RequirementIgnored, reasons)
            : new(change, from, ChangeState.Accepted, []);
    }

    /// <summary>How a block that needs a slot not yet open is marked: "needs slot 2 · 12 more tasks".</summary>
    public static Note NeedsSlot(int slot, int tasks) =>
        Note.Plural("needs slot {0} · {1} more task", "needs slot {0} · {1} more tasks", tasks, slot, tasks);

    // The master's blocks as a change's value: the enemies in id order, or none.
    private static string Blocked(ActiveSlayerMaster master)
    {
        var ids = master
            .BlockSlots.Where(s => s.BlockedEnemy != null)
            .Select(s => s.BlockedEnemy!.Id)
            .Order(StringComparer.Ordinal)
            .ToList();
        return ids.Count == 0 ? Change.None : string.Join(',', ids);
    }
}
