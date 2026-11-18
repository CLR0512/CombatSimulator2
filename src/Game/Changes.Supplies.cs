using GamesByMalcs.Melvor2.Data.Currencies;

namespace MelvorMods.CombatSimulator2.Game;

// What a change gives a copy so the game's own call can take its cost, beyond the items Supplies gives.
internal static partial class Changes
{
    // As for runes: enough to turn a prayer on and fight a while; the tallies top up what fights take after each tick.
    private const long PrayerPointsAdded = 10_000;

    // A character with no prayer points can't turn a prayer on, though a setup search asks what it would be worth:
    // the copy gets some, as it gets the runes a spell takes.
    private static void GivePrayerPoints(Copy copy, Currency currency)
    {
        var wallet = copy.Combat.Prayer.Wallet;
        if (wallet.GetQuantity(currency) <= 0)
            wallet.AddQuantity(currency, PrayerPointsAdded);
    }
}
