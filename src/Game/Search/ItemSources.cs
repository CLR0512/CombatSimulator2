using GamesByMalcs.Melvor2.Components.ArtisanAction;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.CombatLoot;
using GamesByMalcs.Melvor2.Components.Experience;
using GamesByMalcs.Melvor2.Components.ItemUpgrading;
using GamesByMalcs.Melvor2.Data.DropTables;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Entities.Firemaking;
using GamesByMalcs.Melvor2.Entities.Fishing;
using GamesByMalcs.Melvor2.Entities.Quests;
using GamesByMalcs.Melvor2.Entities.Shop;
using GamesByMalcs.Melvor2.Entities.Thieving;
using GamesByMalcs.Melvor2.Random;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Search;

/// <summary>
/// Where an item comes from, read from the game's data, in this order: an enemy that drops it, a
/// dungeon that rewards it, a shop purchase that gives it, a recipe that makes it, an upgrade that ends in it, an
/// item that holds it when opened, the Sacrificial Anvil's collectables, a Thieving target's rare and completion
/// drops, a Firemaking or Fishing product, and a quest's rewards. An item none of them names has no source.
/// Mastery Guild tasks and spells only need items, so they are no source.
/// </summary>
internal static class ItemSources
{
    /// <summary>
    /// Every source of each item, in the order above, each with the enemy or dungeon whose fight gives it (null for
    /// any other), so the spoiler guard can tell an item only hidden fights give.
    /// </summary>
    public static Dictionary<Item, List<ItemSource>> All(MelvorGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var sources = new Dictionary<Item, List<ItemSource>>();
        void Give(Item item, ItemSource source)
        {
            if (!sources.TryGetValue(item, out var list))
                sources[item] = list = [];
            list.Add(source);
        }
        void GiveAll<T>(IReadOnlyWeightedTable<T> table, ItemSource source)
            where T : WeightedItemDrop
        {
            foreach (var entry in table.Items)
                Give(entry.Item, source);
        }
        void Drops(CombatDrops drops, ItemSource source)
        {
            foreach (var item in drops.FixedItems)
                Give(item.Item, source);
            foreach (var table in drops.ItemTables)
            foreach (var entry in table.Table.Items)
                Give(entry.Item, source);
        }
        foreach (var enemy in game.Data.GetDataRegistryOutput<Enemy>().GetAllObjects())
        {
            var name = Note.Text(enemy.DisplayName.Text);
            // Translators: How an item is got, in a gear search answer's hover: {0} the monster that drops it.
            Drops(enemy.Drops, new ItemSource(name, enemy.Id, Detail: Note.Of("Dropped by {0}", name)));
        }
        foreach (var dungeon in game.Data.GetDataRegistryOutput<Dungeon>().GetAllObjects())
        {
            var name = Note.Text(dungeon.DisplayName.Text);
            // Translators: How an item is got, in a gear search answer's hover: {0} the dungeon whose rewards hold it.
            Drops(dungeon.Rewards, new ItemSource(name, dungeon.Id, Detail: Note.Of("Reward from {0}", name)));
        }
        foreach (var purchase in game.Data.GetDataRegistryOutput<ShopPurchase>().GetAllObjects())
        foreach (var given in purchase.Provides.Items)
            Give(
                given.Item,
                new ItemSource(
                    Note.Of("Shop"),
                    Requirements: purchase.Requirements,
                    // Translators: How an item is got, in a gear search answer's hover: buying it in the shop.
                    Detail: Note.Of("Bought in the shop")
                )
            );
        foreach (var recipe in game.Data.GetDataRegistryOutput<ArtisanRecipe>().GetAllObjects())
        foreach (var product in recipe.Products)
            Give(
                product.Item,
                new ItemSource(
                    Note.Text(recipe.Category.DisplayName.Text),
                    Requirements: recipe.Requirements,
                    Detail: Made(recipe)
                )
            );
        foreach (var upgrade in game.Data.GetDataRegistryOutput<ItemUpgrade>().GetAllObjects())
            Give(
                upgrade.UpgradedItem,
                new ItemSource(
                    Note.Of("Upgrade"),
                    Detail: upgrade.RootItems.Count > 0
                        // Translators: How an item is got, in a gear search answer's hover: {0} the item upgraded into it.
                        ? Note.Of(
                            "Upgraded from {0}",
                            Note.Text(LiveGame.NameOf(game, upgrade.RootItems[0].Id, masked: false))
                        )
                        // Translators: How an item is got, in a gear search answer's hover: upgrading another item.
                        : Note.Of("Upgraded from another item")
                )
            );
        foreach (var holder in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
            if (holder.Capabilities.TryGet<OpenableCapability>(out var openable))
            {
                var name = Note.Text(LiveGame.NameOf(game, holder.Id, masked: false));
                // Translators: How an item is got, in a gear search answer's hover: {0} the chest or bag it comes out of.
                GiveAll(
                    openable.DropTable,
                    new ItemSource(name, Holder: holder, Detail: Note.Of("Opened from {0}", name))
                );
            }
        foreach (var mode in game.Data.GetDataRegistryOutput<SacrificialAnvilRewardMode>().GetAllObjects())
        {
            var name = Note.Text(mode.DisplayName.Text);
            GiveAll(
                mode.Collectables,
                new ItemSource(
                    name,
                    Requirements: mode.UnlockRequirements,
                    // Translators: How an item is got, in a gear search answer's hover: {0} the Sacrificial Anvil's reward mode.
                    Detail: Note.Of("Sacrificial Anvil: {0}", name)
                )
            );
        }
        foreach (var thieving in game.Data.GetDataRegistryOutput<ThievingRecipe>().GetAllObjects())
        {
            var target = Note.Text(thieving.DisplayName.Text);
            var from = new ItemSource(
                // Translators: Where an item comes from: a Thieving target that drops it, by the target's name.
                Note.Of("Thieving: {0}", target),
                Requirements: thieving.Requirements,
                // Translators: How an item is got, in a gear search answer's hover: {0} the Thieving target it's stolen from.
                Detail: Note.Of("Stolen from {0} in Thieving", target)
            );
            GiveAll(thieving.RareDropTable, from);
            GiveAll(thieving.CompletionDropTable, from);
        }
        foreach (var log in game.Data.GetDataRegistryOutput<FiremakingRecipe>().GetAllObjects())
        foreach (var product in log.Products)
            // Translators: Where an item comes from: burning a log in Firemaking, by the log's name.
            Give(
                product.Item,
                new ItemSource(
                    Note.Of("Firemaking: {0}", Note.Text(log.DisplayName.Text)),
                    Requirements: log.Requirements,
                    // Translators: How an item is got, in a gear search answer's hover: {0} the log whose burning finds it.
                    Detail: Note.Of("Found burning {0} in Firemaking", Note.Text(log.DisplayName.Text))
                )
            );
        foreach (var fish in game.Data.GetDataRegistryOutput<FishingRecipe>().GetAllObjects())
        foreach (var product in fish.SpecialProducts)
            // Translators: Where an item comes from: a special catch while Fishing, by the fishing spot's name.
            Give(
                product.Item,
                new ItemSource(
                    Note.Of("Fishing: {0}", Note.Text(fish.DisplayName.Text)),
                    Requirements: fish.Requirements,
                    // Translators: How an item is got, in a gear search answer's hover: {0} the fishing spot it's caught at.
                    Detail: Note.Of("Caught fishing {0}", Note.Text(fish.DisplayName.Text))
                )
            );
        foreach (var quest in game.Data.GetDataRegistryOutput<Quest>().GetAllObjects())
        foreach (var reward in quest.Rewards.Items)
            Give(
                reward.Item,
                new ItemSource(
                    Note.Text(quest.DisplayName.Text),
                    Requirements: quest.Requirements,
                    // Translators: How an item is got, in a gear search answer's hover: {0} the quest that rewards it.
                    Detail: Note.Of("Reward for the quest {0}", Note.Text(quest.DisplayName.Text))
                )
            );
        return sources;
    }

    // A recipe's product: made in the skill whose level the recipe needs, under its group's name.
    private static Note Made(ArtisanRecipe recipe)
    {
        var group = Note.Text(recipe.Category.DisplayName.Text);
        return recipe.Requirements?.Conditions.OfType<LevelCondition>().FirstOrDefault()?.Component?.Entity is { } skill
            // Translators: How an item is got, in a gear search answer's hover: {0} the skill it's made in, {1} the recipe group.
            ? Note.Of("Made in {0}: {1}", Note.Text(skill.DisplayName.Text), group)
            // Translators: How an item is got, in a gear search answer's hover: {0} the recipe group it's made under.
            : Note.Of("Made: {0}", group);
    }

    /// <summary>
    /// Whether the character can reach a source of each item now, by the game's own checks in
    /// <paramref name="game"/>: a drop from a target it can fight (the check targets use), a source whose requirements
    /// are met, or an item it can reach that holds it. True when one source can be reached, false when none can, null
    /// when none can and some can't be judged (an upgrade).
    /// </summary>
    public static Dictionary<Item, bool?> Reachable(MelvorGame game, Dictionary<Item, List<ItemSource>> sources)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(sources);
        var fights = Targets
            .All(game)
            .Where(t => t.Kind is TargetKind.Enemy or TargetKind.Dungeon && Targets.Check(game, t).Eligible)
            .Select(t => t.Enemy ?? t.Area)
            .ToHashSet(StringComparer.Ordinal);
        var reached = new Dictionary<Item, bool?>();
        // A chest inside its own chest would loop: an item being judged counts as not judged.
        var judging = new HashSet<Item>();
        bool? Of(Item item)
        {
            if (reached.TryGetValue(item, out var known))
                return known;
            if (!sources.TryGetValue(item, out var from) || !judging.Add(item))
                return null;
            bool? result = false;
            foreach (var source in from)
            {
                bool? one =
                    source.Fight != null ? fights.Contains(source.Fight)
                    : source.Requirements != null ? source.Requirements.AreMet
                    : source.Holder != null ? Of(source.Holder)
                    : null;
                if (one == true)
                {
                    result = true;
                    break;
                }
                if (one == null)
                    result = null;
            }
            judging.Remove(item);
            return reached[item] = result;
        }
        foreach (var item in sources.Keys)
            Of(item);
        return reached;
    }
}

/// <summary>
/// One way to get an item: its name as shown, and what decides whether it can be reached: the enemy or dungeon whose
/// fight gives it, the requirements the game checks, or the item that holds it when opened; none for an upgrade.
/// <see cref="Detail"/> says how it is got, in words a hover can show whole: "Dropped by …", "Opened from …".
/// </summary>
internal sealed record ItemSource(
    Note Name,
    string? Fight = null,
    GamesByMalcs.Melvor2.Conditions.ConditionGroup? Requirements = null,
    Item? Holder = null,
    Note? Detail = null
);
