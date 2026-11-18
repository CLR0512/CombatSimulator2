namespace MelvorMods.CombatSimulator2.Game;

internal sealed partial class Adjustments
{
    private Dictionary<string, long> _startHoldings = [];

    partial void TakeStartHoldings() => _startHoldings = Holdings();

    /// <summary>
    /// What differs from the start (XP, bank items, currencies), for checks. Empty when levels are held, loot is
    /// cleared and consumables are topped up.
    /// </summary>
    public IReadOnlyList<string> Drift()
    {
        var drift = new List<string>();
        foreach (var leveled in _copy.Game.LeveledEntities)
        {
            if (_startXp.TryGetValue(leveled.Experience, out var start) && leveled.Experience.Xp != start)
                drift.Add($"xp {leveled.Id} {start} → {leveled.Experience.Xp}");
        }
        var now = Holdings();
        foreach (var key in now.Keys.Union(_startHoldings.Keys))
        {
            var before = _startHoldings.GetValueOrDefault(key);
            var after = now.GetValueOrDefault(key);
            if (before != after)
                drift.Add($"{key} {before} → {after}");
        }
        return drift;
    }

    private Dictionary<string, long> Holdings()
    {
        var holdings = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var stack in _copy.Bank.Inventory.Stacks)
        {
            if (!stack.IsPlaceholder)
                holdings["bank " + stack.Item.Id] =
                    holdings.GetValueOrDefault("bank " + stack.Item.Id) + stack.Quantity;
        }
        foreach (var (currency, stack) in _copy.Bank.Wallet.Stacks)
            holdings["wallet " + currency.Id] = stack.Quantity;
        return holdings;
    }
}
