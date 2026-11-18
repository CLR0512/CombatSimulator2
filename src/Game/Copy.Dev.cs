using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

internal sealed partial class Copy
{
    /// <summary>For checks: the bank's stacks in order (item, quantity, placeholder).</summary>
    public List<string> BankStacks()
    {
        using var scope = Guards.Enter();
        return
        [
            .. Bank.Inventory.Stacks.Select(s =>
                $"{Ids.Brief(s.Item.Id)}×{s.Quantity}{(s.IsPlaceholder ? "(p)" : "")}{(s.IsLocked ? "(l)" : "")}"
            ),
        ];
    }

    /// <summary>For checks: the bank's placeholder, sort-order, default-tab, locked and tab counts.</summary>
    public string BankExtras()
    {
        using var scope = Guards.Enter();
        var bank = Bank.Inventory;
        return $"placeholders {bank.PlaceholderItems.Count} sort {bank._customSortOrder.Count} default-tabs {bank.DefaultItemTabs.Count} locked {bank.LockedItems.Count} tabs {bank._inventoryTabs.Count} new-tab {bank.DefaultNewItemTab}";
    }

    /// <summary>For checks: each mastery guild with points and rank.</summary>
    public string Guilds()
    {
        using var scope = Guards.Enter();
        return string.Join(
            " ",
            Game.Entities.GetAllObjects()
                .SelectMany(e =>
                    e.Components.Values.OfType<GamesByMalcs.Melvor2.Components.MasteryGuild.MasteryGuildComponent>()
                )
                .Where(g =>
                    g.CurrentPoints > 0
                    && g.Entity.Id
                        is "MelvorBase:Hitpoints"
                            or "MelvorBase:Attack"
                            or "MelvorBase:Strength"
                            or "MelvorBase:Defence"
                )
                .Select(g =>
                    $"{Ids.Brief(g.Entity.Id)}:{g.CurrentPoints}/{Ids.Brief(g.CurrentRank.Id)} done {g.Categories.Values.Sum(c => c.CompletedTasks)} complete {g.ActiveTasks.Values.Count(t => t.IsComplete)}"
                )
        );
    }

    /// <summary>For checks: every modifier source (type and id) with its values' count and multiplier.</summary>
    public List<string> ModifierEntries()
    {
        using var scope = Guards.Enter();
        var entries = new List<string>();
        foreach (var (source, values) in Game.Modifiers.Values)
        {
            foreach (var (value, multiplier) in values)
                entries.Add(
                    $"{source.GetType().Name}:{(source as GamesByMalcs.Melvor2.Data.INamespacedObject)?.Id ?? source.ToString()}#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(source)}/{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value)}×{multiplier}"
                );
            if (values.Count == 0)
                entries.Add($"{source.GetType().Name} (empty)");
        }
        return entries;
    }
}
