using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.CombatLoot;
using GamesByMalcs.Melvor2.Components.ItemBurying;
using GamesByMalcs.Melvor2.Components.RareEvents;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// A target's rare drops as the game defines them, read on the copy when the target starts: loot entries below
/// <see cref="RareDrops.Threshold"/> per kill with the chance the game's own enemy panel shows (the table's chance
/// times the item's share of its weight; no modifier changes it), a dungeon's reward entries per completion (enemies
/// in dungeons drop no loot), and every rare event the combat rolls each minute, with its unmet requirements. Each
/// loot or reward entry carries the items a drop gives with the setup's chances to double loot, as the game rolls
/// them.
/// </summary>
internal static class Drops
{
    public static List<DropChance> Of(CombatEntity combat, Enemy? enemy, Dungeon? dungeon)
    {
        var drops = new List<DropChance>();
        var table = dungeon?.Rewards ?? enemy?.Drops;
        var events = combat.Config.GlobalTimedEvents;
        if (table != null)
        {
            var stats = combat.Loot.Stats;
            var doubling = new Doubling(
                Probability(stats.ItemDoublingChance.Total.Value),
                Probability(stats.BuriableDoublingChance.Total.Value)
            );
            // The run counts these items' other drops together with the rare entry's, so only the counts stand.
            var otherwise = table
                .FixedItems.Select(f => f.Item.Id)
                .Concat(
                    table.ItemTables.SelectMany(w =>
                        w.Table.Items.Where(e => w.Chance * w.Table.GetItemChance(e) >= RareDrops.Threshold)
                            .Select(e => e.Item.Id)
                    )
                )
                .Concat(events.OfType<RareItemEvent>().Select(r => r.Item.Id))
                .ToHashSet(StringComparer.Ordinal);
            if (dungeon != null)
                Entries(table, DropSource.Reward, ChancePer.Completion, doubling, otherwise, drops);
            else
                Entries(table, DropSource.Loot, ChancePer.Kill, doubling, otherwise, drops);
        }
        foreach (var rare in events)
        {
            drops.Add(
                new DropChance
                {
                    Source = DropSource.RareEvent,
                    Id = rare.Id,
                    Item = (rare as RareItemEvent)?.Item.Id,
                    Chance = rare.Chance,
                    Per = ChancePer.MinuteInCombat,
                    Unmet = Targets.Unmet(rare.Requirements),
                }
            );
        }
        return drops;
    }

    private sealed record Doubling(double Item, double Buryable);

    private static double Probability(double chance) => Math.Clamp(chance, 0, 1);

    private static void Entries(
        CombatDrops table,
        DropSource source,
        ChancePer per,
        Doubling doubling,
        HashSet<string> otherwise,
        List<DropChance> into
    )
    {
        foreach (var weighted in table.ItemTables)
        {
            foreach (var entry in weighted.Table.Items)
            {
                var chance = weighted.Chance * weighted.Table.GetItemChance(entry);
                if (chance >= RareDrops.Threshold)
                    continue;
                var item = entry.Item;
                double? quantity = null;
                if (!otherwise.Contains(item.Id))
                {
                    var buryable = item.Capabilities.TryGet<BuryCapability>(out _) ? doubling.Buryable : 0;
                    quantity = (entry.MinQuantity + entry.MaxQuantity) / 2.0 * (1 + doubling.Item) * (1 + buryable);
                }
                into.Add(
                    new DropChance
                    {
                        Source = source,
                        Id = item.Id,
                        Chance = chance,
                        Per = per,
                        Quantity = quantity,
                    }
                );
            }
        }
    }
}
