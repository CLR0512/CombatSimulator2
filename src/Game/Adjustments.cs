using GamesByMalcs.Melvor2.Components.Experience;
using GamesByMalcs.Melvor2.Components.Inventories;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The adjustments a copy needs so it doesn't drift from the setup being measured: a clean start, slayer off, loot
/// cleared, consumables topped up and levels held. Each applies to the copy only, through the game's own APIs, and
/// each is listed in plain words in every result's assumptions.
/// </summary>
internal sealed partial class Adjustments
{
    public static readonly IReadOnlyList<Note> Assumptions = [.. Assumption.All.Select(a => a.Note)];

    private readonly Copy _copy;
    private readonly Tallying _tallying;
    private readonly Dictionary<ExperienceComponent, double> _startXp = [];

    public Adjustments(Copy copy, Tallying tallying)
    {
        _copy = copy;
        _tallying = tallying;
    }

    /// <summary>Gives a clean start and turns slayer off, after the snapshot is applied and reseeded.</summary>
    public void Start()
    {
        var combat = _copy.Combat;
        Write(() =>
        {
            // A snapshot taken mid-fight would resume that fight.
            combat.Action.TryStop();
            combat.Player.Health.Set(combat.Player.Health.Max);
            // AutoSlayer could switch the copy to another enemy.
            if (combat.SlayerTasks != null)
            {
                foreach (var master in combat.SlayerTasks.ActiveMasters.Values)
                    master.AutoSlayerEnabled = false;
            }
        });
        foreach (var leveled in _copy.Game.LeveledEntities)
            _startXp[leveled.Experience] = leveled.Experience.Xp;
        TakeStartHoldings();
    }

    /// <summary>Clears counted loot from the bank and wallet, and tops consumables back up, after every tick.</summary>
    public void AfterTick()
    {
        if (_tallying.HasCorrections)
            Correct();
    }

    // Apart from AfterTick, so the closure is only made on the ticks that need it: the compiler makes a method's
    // closure on entry, which on every tick was a tenth of everything a fight allocates.
    private void Correct()
    {
        var bank = _copy.Bank.Inventory;
        var wallet = _copy.Bank.Wallet;
        Write(() =>
        {
            foreach (var (item, quantity) in _tallying.ItemsToRemove)
                bank.RemoveQuantity(item, quantity);
            foreach (var (currency, quantity) in _tallying.CurrencyToRemove)
                wallet.RemoveQuantity(currency, quantity);
            foreach (var (item, quantity) in _tallying.ItemsToTopUp)
                bank.AddQuantity(item, quantity, AddItemFlags.None);
            foreach (var (currency, quantity) in _tallying.CurrencyToTopUp)
                wallet.AddQuantity(currency, quantity);
            foreach (var (slot, quantity) in _tallying.EquipmentToTopUp)
            {
                if (!slot.IsEmpty)
                    slot.AddQuantity(quantity);
            }
            foreach (var (slot, quantity) in _tallying.ToolbeltToTopUp)
            {
                if (slot.Equipped != null)
                    slot.AddQuantity(quantity);
            }
        });
        _tallying.ClearCorrections();
    }

    /// <summary>Sets every skill's XP back to its snapshot value after each trial, so levels are held.</summary>
    public void HoldLevels() =>
        Write(() =>
        {
            foreach (var (experience, start) in _startXp)
            {
                if (experience.Xp != start)
                    experience.SetXp(start);
            }
        });

    private void Write(Action write)
    {
        _tallying.Writing = true;
        try
        {
            write();
        }
        finally
        {
            _tallying.Writing = false;
        }
    }

    // Dev builds only: the bank and wallet at the start, for Drift.
    partial void TakeStartHoldings();
}
