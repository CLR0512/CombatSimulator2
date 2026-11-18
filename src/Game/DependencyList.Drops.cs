namespace MelvorMods.CombatSimulator2.Game;

// The catalogue's rows for a target's rare drops (Drops): loot and reward tables with what one drop gives under the
// setup's chances to double loot, and the combat config's rare events.
internal static partial class Dependencies
{
    // A property, not a field: the table is built while this type's fields are still being set, in no promised
    // order across its files.
    private static IEnumerable<Dependency> DropTables =>
        [
            .. T(Drops, "Entities.Combat.Enemy", "property:Drops"),
            .. T(Drops, "Components.CombatAreas.Dungeon", "property:Rewards"),
            .. T(Drops, "Components.CombatLoot.CombatDrops", "property:ItemTables"),
            .. T(Drops, "Data.DropTables.ItemDropTableChance", "property:Chance", "property:Table"),
            .. T(Drops, "Random.IReadOnlyWeightedTable`1", "GetItemChance", "property:Items"),
            .. T(
                Drops,
                "Data.Items.ItemQuantityRange",
                "property:Item",
                "property:MinQuantity",
                "property:MaxQuantity"
            ),
            .. T(Drops, "Components.CombatLoot.CombatDrops", "property:FixedItems"),
            .. T(Drops, "Entities.Combat.CombatEntity", "property:Loot"),
            .. T(Drops, "Components.CombatLoot.CombatLootComponent", "property:Stats"),
            .. T(
                Drops,
                "Components.CombatLoot.CombatLootStats",
                "property:ItemDoublingChance",
                "property:BuriableDoublingChance"
            ),
            .. T(Drops, "Stats.Nodes.ModifierStatNodes", "property:Total"),
            .. T(Drops, "Stats.Nodes.ValueStatNode`1", "property:Value"),
            .. T(Drops, "Data.Items.Item", "property:Capabilities"),
            .. T(Drops, "Components.ItemBurying.BuryCapability"),
            .. T(Drops, "Entities.Combat.CombatEntityConfig", "property:GlobalTimedEvents"),
            .. T(Drops, "Components.RareEvents.RareEvent", "property:Chance", "property:Requirements"),
            .. T(Drops, "Components.RareEvents.RareItemEvent", "property:Item"),
        ];
}
