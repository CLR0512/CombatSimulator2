using GamesByMalcs.Melvor2.Components.ArtisanAction;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.CombatLoot;
using GamesByMalcs.Melvor2.Components.ItemUpgrading;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Entities.Shop;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Search;

/// <summary>
/// Where an item comes from, read from the game's data and never judged: the first of an enemy that drops it, a
/// dungeon that rewards it, a shop purchase that gives it, a recipe that makes it, and an upgrade that ends in
/// it. An item none of them names has no source.
/// </summary>
internal static class ItemSources
{
    public static Dictionary<Item, Note> Read(MelvorGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var sources = new Dictionary<Item, Note>();
        foreach (var enemy in game.Data.GetDataRegistryOutput<Enemy>().GetAllObjects())
            Add(sources, enemy.Drops, Note.Text(enemy.DisplayName.Text));
        foreach (var dungeon in game.Data.GetDataRegistryOutput<Dungeon>().GetAllObjects())
            Add(sources, dungeon.Rewards, Note.Text(dungeon.DisplayName.Text));
        foreach (var purchase in game.Data.GetDataRegistryOutput<ShopPurchase>().GetAllObjects())
        foreach (var given in purchase.Provides.Items)
            sources.TryAdd(given.Item, Note.Of("Shop"));
        foreach (var recipe in game.Data.GetDataRegistryOutput<ArtisanRecipe>().GetAllObjects())
        foreach (var product in recipe.Products)
            sources.TryAdd(product.Item, Note.Text(recipe.Category.DisplayName.Text));
        foreach (var upgrade in game.Data.GetDataRegistryOutput<ItemUpgrade>().GetAllObjects())
            sources.TryAdd(upgrade.UpgradedItem, Note.Of("Upgrade"));
        return sources;
    }

    private static void Add(Dictionary<Item, Note> sources, CombatDrops drops, Note source)
    {
        foreach (var item in drops.FixedItems)
            sources.TryAdd(item.Item, source);
        foreach (var table in drops.ItemTables)
        foreach (var entry in table.Table.Items)
            sources.TryAdd(entry.Item, source);
    }
}
