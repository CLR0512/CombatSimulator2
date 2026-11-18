using GamesByMalcs.Melvor2.Components.Shop;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Entities.Shop;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// The shop's purchases, a group per shop category in the game's order. A purchase can be bought up to its limit and
/// never refunded; one the build ships without its data is shown and can't be changed.
/// </summary>
internal static class ShopPage
{
    private const int NoLimit = Change.HighestCount;

    public static void Values(MelvorGame game, Dictionary<ChangeKey, int> values)
    {
        if (!Dependencies.IsAvailable(Dependencies.Shop) || ShopChange.Shop(game) is not { } shop)
            return;
        foreach (var purchase in shop.Config.Purchases)
            values[new ChangeKey(ChangeKind.Shop, purchase.Id)] = shop.GetUpgradesPurchased(purchase);
    }

    public static SetupPage Read(MelvorGame game, IReadOnlyDictionary<ChangeKey, int> save)
    {
        if (!Dependencies.IsAvailable(Dependencies.Shop))
            return PageReader.Unsupported(SetupPage.Shop);
        if (ShopChange.Shop(game) is not { } shop || shop.Config.Purchases.Count == 0)
            return PageReader.Page(SetupPage.Shop, PageState.NoData, Note.Of("Not in this build"));
        var unlocking = Unlocking(game);
        var groups = new List<PageGroup>();
        foreach (var category in shop.Config.Categories)
        {
            var entries = shop
                .Config.Purchases.Where(p => p.Category == category)
                .Select(p => Entry(shop, p, save, unlocking.Contains(p)))
                .ToList();
            if (entries.Count > 0)
                groups.Add(
                    new PageGroup(
                        category.Id,
                        category.DisplayName.Text,
                        category.Id,
                        Note.Of("{0} of {1} bought", entries.Count(e => e.Value > 0), entries.Count),
                        entries
                    )
                );
        }
        return new SetupPage(SetupPage.Shop, PageSection.Account, PageState.Changeable, null, [], groups);
    }

    private static PageEntry Entry(
        ShopComponent shop,
        ShopPurchase purchase,
        IReadOnlyDictionary<ChangeKey, int> save,
        bool unlocks
    )
    {
        var key = new ChangeKey(ChangeKind.Shop, purchase.Id);
        var bought = shop.GetUpgradesPurchased(purchase);
        var name = purchase.DisplayName.Text;
        var gives = purchase.Provides.Bonuses is { } bonuses
            ? SetupSheets.Describe(ModifierGroup.Shop, purchase.Id, name, bonuses)
            : [];
        var unmet = Targets.Unmet(purchase.Requirements);
        return new PageEntry(
            purchase.Id,
            name,
            purchase.Id,
            purchase.DefaultBuyLimit == 1 ? EntryKind.Toggle : EntryKind.Count,
            bought,
            purchase.DefaultBuyLimit > 0 ? purchase.DefaultBuyLimit : NoLimit,
            PageReader.Saved(save, key, bought),
            unmet,
            gives,
            key,
            null,
            unlocks || (purchase.Provides.Bonuses is { } felt && CombatReach.Of(felt)),
            Description: Described(purchase.Description?.Text)
        );
    }

    private static string? Described(string? text)
    {
        var plain = text == null ? "" : Targets.Plain(text);
        return plain.Length > 0 ? plain : null;
    }

    // The purchases something a setup uses waits for: an equipment, toolbelt, loadout or prayer slot, or an area.
    private static HashSet<ShopPurchase> Unlocking(MelvorGame game)
    {
        var data = game.Data;
        IEnumerable<ConditionGroup> groups =
        [
            .. All<GamesByMalcs.Melvor2.Components.Equipment.EquipmentSlot>().Select(s => s.UnlockRequirements),
            .. All<GamesByMalcs.Melvor2.Components.Toolbelt.ToolbeltSlot>().Select(s => s.UnlockRequirements),
            .. All<GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot>().Select(s => s.UnlockRequirements),
            .. All<GamesByMalcs.Melvor2.Components.Prayers.PrayerSlot>().Select(s => s.UnlockRequirements),
            .. All<GamesByMalcs.Melvor2.Components.CombatAreas.BaseCombatArea>().Select(a => a.UnlockRequirements),
        ];
        return [.. groups.SelectMany(g => g.Conditions).OfType<ShopPurchaseCondition>().Select(c => c.ShopPurchase)];

        IEnumerable<T> All<T>()
            where T : GamesByMalcs.Melvor2.Data.INamespacedObject => data.GetDataRegistryOutput<T>().GetAllObjects();
    }
}
