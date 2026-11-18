using System.Globalization;
using GamesByMalcs.Melvor2.Core.InventoryWalletTransactions;
using GamesByMalcs.Melvor2.Entities.Shop;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// A shop purchase, bought through the shop's own call after the copy is given what it costs. The game has no
/// refund, so a count can only go up. Copies only.
/// </summary>
internal static class ShopChange
{
    /// <summary>The shop of <paramref name="game"/>; null when the build has none.</summary>
    public static GamesByMalcs.Melvor2.Components.Shop.ShopComponent? Shop(GamesByMalcs.Melvor2.Game game) =>
        game.Entities.GetAllObjects().OfType<ShopEntity>().FirstOrDefault()?.Shop;

    public static ChangeOutcome Apply(Copy copy, Change change, bool ignoreRequirements)
    {
        if (
            Shop(copy.Game) is not { } shop
            || !copy.Game.Data.TryGetObject<ShopPurchase>(change.Subject!, out var purchase)
        )
            return Changes.Missing(change, null);
        var bought = shop.GetUpgradesPurchased(purchase);
        var from = bought.ToString(CultureInfo.InvariantCulture);
        var target = change.Number!.Value;
        if (target == bought)
            return new(change, from, ChangeState.NoChange, []);
        if (target < bought)
            return Changes.Refused(change, from, Note.Of("the game has no refund"));
        if (purchase.DefaultBuyLimit > 0 && target > purchase.DefaultBuyLimit)
            return Changes.Refused(change, from, Note.Of("the shop sells at most {0}", purchase.DefaultBuyLimit));
        var reasons = Targets.Unmet(purchase.Requirements);
        if (reasons.Count > 0 && !ignoreRequirements)
            return Changes.Refused(change, from, [.. reasons]);
        var quantity = target - bought;
        if (!Supplies.Ingredients(copy, purchase.Costs, quantity))
            return Changes.Refused(change, from, Note.Of("bank full"));
        InventoryWalletTransactionResult result;
        using (
            reasons.Count > 0 ? RequirementsIgnored.Enter(purchase.Requirements) : default(RequirementsIgnored.Scope?)
        )
            result = shop.ProcessPurchase(purchase, quantity);
        if (result != InventoryWalletTransactionResult.Success)
            return Changes.Refused(
                change,
                from,
                result switch
                {
                    InventoryWalletTransactionResult.NotEnoughInventorySpace => Note.Of("bank full"),
                    InventoryWalletTransactionResult.NotEnoughCurrencySpace => Note.Of("the wallet is full"),
                    InventoryWalletTransactionResult.RequirementsNotMet => Note.Of(
                        "the game still asks for its requirements"
                    ),
                    _ => Note.Of("the game didn't sell it"),
                }
            );
        if (shop.GetUpgradesPurchased(purchase) != target)
            return Changes.Refused(
                change,
                from,
                Note.Of("the game sold {0}", shop.GetUpgradesPurchased(purchase) - bought)
            );
        return reasons.Count > 0
            ? new(change, from, ChangeState.RequirementIgnored, reasons)
            : new(change, from, ChangeState.Accepted, []);
    }
}
