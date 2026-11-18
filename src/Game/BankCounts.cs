using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Data.Items;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// How many of each item a copy's bank held at one moment, and in which order, so what a change supplied or moved
/// can be undone. A copy's bank can't be read back from its bytes a second time and a reset leaves it as it is, so
/// the counts and the order are put back through the bank's own calls. The order matters only to the copy being as
/// prepared: an item taken out whole leaves the bank, and comes back at its end.
/// </summary>
internal sealed class BankCounts
{
    private readonly Copy _copy;
    private readonly Dictionary<Item, int> _kept;
    private readonly List<List<Item>> _order;

    private BankCounts(Copy copy, Dictionary<Item, int> kept, List<List<Item>> order)
    {
        _copy = copy;
        _kept = kept;
        _order = order;
    }

    /// <summary>The counts of <paramref name="copy"/>'s bank as it is now.</summary>
    public static BankCounts Of(Copy copy)
    {
        using var scope = Guards.Enter();
        return new(copy, Read(copy), [.. copy.Bank.Inventory.InventoryTabs.Select(Items)]);
    }

    /// <summary>
    /// Puts each item's count back. Call it before the copy's reset, which then reports the bank if anything else in
    /// it still differs.
    /// </summary>
    public void Restore()
    {
        using var scope = Guards.Enter();
        var bank = _copy.Bank.Inventory;
        var now = Read(_copy);
        foreach (var (item, held) in now)
        {
            var kept = _kept.GetValueOrDefault(item);
            if (held > kept)
                bank.RemoveQuantity(item, held - kept);
        }
        foreach (var (item, kept) in _kept)
        {
            var held = now.GetValueOrDefault(item);
            if (held < kept)
                bank.AddQuantity(item, kept - held, AddItemFlags.None);
        }
        Reorder(bank);
    }

    // Each stack back to the place it had in its tab. A tab that still lacks a stack or has one more is left as it
    // is: the reset reports the bank then.
    private void Reorder(GamesByMalcs.Melvor2.Entities.Bank.BankInventoryComponent bank)
    {
        var tabs = bank.InventoryTabs;
        for (var t = 0; t < _order.Count && t < tabs.Count; t++)
        {
            var kept = _order[t];
            var now = Items(tabs[t]);
            for (var i = 0; i < kept.Count && i < now.Count; i++)
            {
                if (ReferenceEquals(now[i], kept[i]))
                    continue;
                var from = now.FindIndex(i + 1, item => ReferenceEquals(item, kept[i]));
                if (from < 0)
                    break;
                tabs[t].MoveStack(from, i);
                now.RemoveAt(from);
                now.Insert(i, kept[i]);
            }
        }
    }

    private static List<Item> Items(StackableInventory tab) => [.. tab.Stacks.Select(stack => stack.Item)];

    private static Dictionary<Item, int> Read(Copy copy)
    {
        var counts = new Dictionary<Item, int>();
        // A stack marked as a placeholder still holds its quantity, so it counts like any other.
        foreach (var stack in copy.Bank.Inventory.Stacks)
            counts[stack.Item] = counts.GetValueOrDefault(stack.Item) + stack.Quantity;
        return counts;
    }
}
