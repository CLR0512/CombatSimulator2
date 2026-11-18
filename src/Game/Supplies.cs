using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Gives a copy what a change costs, so the game's own call can take it: items into the copy's bank and currency
/// into its wallet. A change asks what a setup would be worth, not whether it is affordable today. Copies only:
/// never the live game.
/// </summary>
internal static class Supplies
{
    /// <summary>
    /// Raises the copy's bank to hold at least <paramref name="quantity"/> of <paramref name="item"/>; false when the
    /// bank has no room for it.
    /// </summary>
    public static bool Item(Copy copy, Item item, int quantity)
    {
        var bank = copy.Bank.Inventory;
        var held = bank.GetQuantity(item);
        return held >= quantity || bank.AddQuantity(item, quantity - held, AddItemFlags.None) > 0;
    }

    /// <summary>
    /// Supplies what <paramref name="costs"/> takes, <paramref name="times"/> over; false when the bank has no room.
    /// </summary>
    public static bool Ingredients(Copy copy, GamesByMalcs.Melvor2.Data.Recipes.RecipeIngredients costs, int times)
    {
        foreach (var item in costs.Items)
        {
            if (!Item(copy, item.Item, item.Quantity * times))
                return false;
        }
        foreach (var currency in costs.Currencies)
            Currency(copy, currency.Currency, currency.Quantity * times);
        return true;
    }

    /// <summary>
    /// Raises the copy's wallet to hold at least <paramref name="quantity"/> of <paramref name="currency"/>.
    /// </summary>
    public static void Currency(Copy copy, Currency currency, long quantity)
    {
        var wallet = copy.Bank.Wallet;
        var held = wallet.GetQuantity(currency);
        if (held < quantity)
            wallet.AddQuantity(currency, quantity - held);
    }
}
