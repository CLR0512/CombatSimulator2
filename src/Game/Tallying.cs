using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Components.CurrencyWallets;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Experience;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Components.ItemBurying;
using GamesByMalcs.Melvor2.Components.RareEvents;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Core;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Feeds the open trial's tally from the copy's own events, keyed by game ids with no fixed list.
/// Gains are increases of the bank and wallet from any source (loot, dungeon rewards, rare-event grants);
/// consumables used are decreases of the bank, wallet, equipped stacks and toolbelt charges. Each change is also
/// queued as a correction (loot to clear, use to top up) that <see cref="Adjustments"/> applies after the tick,
/// with <see cref="Writing"/> set so its own writes aren't counted.
/// </summary>
internal sealed class Tallying : IDisposable
{
    private readonly Copy _copy;
    private readonly Dictionary<ExperienceComponent, string> _skills = [];
    private readonly List<(IActiveEquipmentSlot Slot, EventHandler<ValueChangedEventArgs<int>> Handler)> _equipment =
    [];
    private readonly List<(ActiveToolbeltSlot Slot, EventHandler<ValueChangedEventArgs<int>> Handler)> _toolbelt = [];

    public Tallying(Copy copy)
    {
        _copy = copy;
        foreach (var leveled in copy.Game.LeveledEntities)
        {
            _skills[leveled.Experience] = leveled.Id;
            leveled.Experience.XpChanged += XpChanged;
        }
        copy.Bank.Inventory.ItemQuantityChanged += BankChanged;
        copy.Bank.Wallet.CurrencyQuantityChanged += WalletChanged;
        var equipment = copy.Combat.Player.Equipment;
        foreach (var slot in equipment.Config.EquipmentSlots)
        {
            if (!equipment.TryGetActiveSlot(slot, out var active))
                continue;
            EventHandler<ValueChangedEventArgs<int>> handler = (_, e) => EquipmentChanged(active, e);
            active.QuantityChanged += handler;
            _equipment.Add((active, handler));
        }
        foreach (var active in copy.Combat.Toolbelt._activeSlots.Values)
        {
            EventHandler<ValueChangedEventArgs<int>> handler = (_, e) => ToolbeltChanged(active, e);
            active.QuantityChanged += handler;
            _toolbelt.Add((active, handler));
        }
        // Kept in their own methods so a missing member only turns rare events off, never the whole tally.
        if (Dependencies.IsAvailable(Dependencies.RareEvents))
            SubscribeRareEvents();
        RecordsRisk = Dependencies.IsAvailable(Dependencies.Risk);
        if (RecordsRisk)
            SubscribeRisk();
        if (Dependencies.IsAvailable(Dependencies.FightCounts))
            CountFights();
        Totals = Dependencies.IsAvailable(Dependencies.Totals);
        if (Totals)
            SubscribeRunes();
    }

    // Food, potions, runes and what bones could give, counted together beside each item's own count.
    private bool Totals { get; }

    // The game's own count of runes used watches what the spells take from the bank: imbuement costs come out of it too.
    private void SubscribeRunes() => _copy.Combat.MagicSpells.Inventory.ItemAdded += RunesSpent;

    private void UnsubscribeRunes() => _copy.Combat.MagicSpells.Inventory.ItemAdded -= RunesSpent;

    private void RunesSpent(object? sender, ItemAddedEventArgs e)
    {
        if (!Writing && e.Quantity < 0)
            Add(Trial.Used, "runes", -e.Quantity);
    }

    // What burying one of an item gives, with the bank's modifiers; null for an item that can't be buried.
    private readonly Dictionary<Item, (string Currency, long Each)?> _bury = [];

    private void CountBuryable(Item item, long gained)
    {
        if (!_bury.TryGetValue(item, out var bury))
        {
            bury = item.Capabilities.TryGet<BuryCapability>(out var capability)
                ? (capability.CurrencyGain.Currency.Id, _copy.Bank.ItemBurying.GetItemStats(item).RewardPerBury.Value)
                : null;
            _bury[item] = bury;
        }
        if (bury is { } b)
            Add(Trial.Buryable ??= new(StringComparer.Ordinal), b.Currency, b.Each * gained);
    }

    // Eaten food is also counted together, as food per time says how much to bring whatever is eaten.
    private void CountFood(Item item, long used)
    {
        if (item.Capabilities.TryGet<FoodCapability>(out _))
            Add(Trial.Used, "food", used);
    }

    private void CountFights() => Counting = new FightCounting(_copy, this);

    /// <summary>
    /// What the game reported in the open trial's fights; null when the game build lacks the members, and results
    /// then have no counts.
    /// </summary>
    public FightCounting? Counting { get; private set; }

    private void SubscribeRareEvents() => _copy.Combat.RareEvents.EventObserved += RareEventObserved;

    private void UnsubscribeRareEvents() => _copy.Combat.RareEvents.EventObserved -= RareEventObserved;

    private void SubscribeRisk() => _copy.Combat.Player.GlobalHealth.Damaged += PlayerDamaged;

    private void UnsubscribeRisk() => _copy.Combat.Player.GlobalHealth.Damaged -= PlayerDamaged;

    /// <summary>Whether hits and HP are watched (the game build has the members), so the driver records risk.</summary>
    public bool RecordsRisk { get; }

    /// <summary>The open trial's hits; the driver takes them at each trial's end.</summary>
    public Hits TrialHits { get; } = new();

    /// <summary>The open dungeon fight's hits; the driver takes them at each fight's end.</summary>
    public Hits FightHits { get; } = new();

    /// <summary>The open trial's tally; the driver takes it at each trial's end.</summary>
    public Tally Trial { get; } = new();

    /// <summary>Whether CS2 is writing to the copy: changes then are neither counted nor corrected.</summary>
    public bool Writing { get; set; }

    // Corrections for Adjustments.AfterTick.
    public Dictionary<Item, int> ItemsToRemove { get; } = [];
    public Dictionary<Currency, long> CurrencyToRemove { get; } = [];
    public Dictionary<Item, int> ItemsToTopUp { get; } = [];
    public Dictionary<Currency, long> CurrencyToTopUp { get; } = [];
    public Dictionary<ActiveEquipmentSlot, int> EquipmentToTopUp { get; } = [];
    public Dictionary<ActiveToolbeltSlot, int> ToolbeltToTopUp { get; } = [];

    public bool HasCorrections =>
        ItemsToRemove.Count
            + CurrencyToRemove.Count
            + ItemsToTopUp.Count
            + CurrencyToTopUp.Count
            + EquipmentToTopUp.Count
            + ToolbeltToTopUp.Count
        > 0;

    public void ClearCorrections()
    {
        ItemsToRemove.Clear();
        CurrencyToRemove.Clear();
        ItemsToTopUp.Clear();
        CurrencyToTopUp.Clear();
        EquipmentToTopUp.Clear();
        ToolbeltToTopUp.Clear();
    }

    private void XpChanged(object? sender, ValueChangedEventArgs<double> e)
    {
        if (Writing || sender is not ExperienceComponent experience || !_skills.TryGetValue(experience, out var id))
            return;
        var gained = e.NewValue - e.OldValue;
        if (gained > 0)
            Trial.Xp[id] = Trial.Xp.GetValueOrDefault(id) + gained;
    }

    private void BankChanged(object? sender, ItemQuantityChangedEventArgs e)
    {
        if (Writing || e.NewValue == e.OldValue)
            return;
        var id = e.Item.Id;
        if (e.NewValue > e.OldValue)
        {
            var gained = e.NewValue - e.OldValue;
            Trial.Gained[id] = Trial.Gained.GetValueOrDefault(id) + gained;
            ItemsToRemove[e.Item] = ItemsToRemove.GetValueOrDefault(e.Item) + gained;
            if (Totals)
                CountBuryable(e.Item, gained);
        }
        else
        {
            var used = e.OldValue - e.NewValue;
            Add(Trial.Used, "item:" + id, used);
            if (Totals)
                CountFood(e.Item, used);
            ItemsToTopUp[e.Item] = ItemsToTopUp.GetValueOrDefault(e.Item) + used;
        }
    }

    private void WalletChanged(object? sender, CurrencyQuantityChangedEventArgs e)
    {
        if (Writing || e.NewValue == e.OldValue)
            return;
        var id = e.Currency.Id;
        if (e.NewValue > e.OldValue)
        {
            var gained = e.NewValue - e.OldValue;
            Trial.Currency[id] = Trial.Currency.GetValueOrDefault(id) + gained;
            CurrencyToRemove[e.Currency] = CurrencyToRemove.GetValueOrDefault(e.Currency) + gained;
        }
        else
        {
            var used = e.OldValue - e.NewValue;
            Add(Trial.Used, "currency:" + id, used); // prayer points, and any other currency spent in combat
            CurrencyToTopUp[e.Currency] = CurrencyToTopUp.GetValueOrDefault(e.Currency) + used;
        }
    }

    private void EquipmentChanged(IActiveEquipmentSlot slot, ValueChangedEventArgs<int> e)
    {
        if (Writing || e.NewValue >= e.OldValue || slot is not ActiveEquipmentSlot active || active.Equipped == null)
            return;
        var used = e.OldValue - e.NewValue;
        Add(Trial.Used, "ammo:" + active.Equipped.Item.Id, used);
        // Every kind of ammo together, so sets that shoot different arrows or bolts compare on one count.
        Add(Trial.Used, "ammo", used);
        EquipmentToTopUp[active] = EquipmentToTopUp.GetValueOrDefault(active) + used;
    }

    private void ToolbeltChanged(ActiveToolbeltSlot slot, ValueChangedEventArgs<int> e)
    {
        if (Writing || e.NewValue >= e.OldValue || slot.Equipped == null)
            return;
        var used = e.OldValue - e.NewValue;
        Add(Trial.Used, "charges:" + slot.Equipped.Item.Id, used);
        // The toolbelt's charges together: the combat potion slot's as potions, the other slots' as consumables.
        Add(Trial.Used, slot.Slot.FitsType.Id == Changes.CombatPotionSlot ? "potions" : "consumables", used);
        ToolbeltToTopUp[slot] = ToolbeltToTopUp.GetValueOrDefault(slot) + used;
    }

    // The player's inner health raises this right after HP drops, before food or anything else heals: the outer
    // health raises its own event only after auto-eat, so only this one sees the low point. Every cause of damage
    // comes through here: attacks, combat effects and reflect.
    private void PlayerDamaged(object? sender, HealthDamagedEventArgs e)
    {
        if (Writing || sender is not CharacterHealth health)
            return;
        long hit = e.Damage.Modified;
        long hp = health.Current;
        TrialHits.Add(hit, hp, e.Amount);
        FightHits.Add(hit, hp, e.Amount);
        if (e.Cause == HealthChangeCause.Reflect)
            TrialHits.Reflected(hit);
    }

    private void RareEventObserved(object? sender, RareEventObservedEventArgs e)
    {
        if (Writing)
            return;
        Trial.RareEvents[e.Event.Id] = Trial.RareEvents.GetValueOrDefault(e.Event.Id) + 1;
    }

    private static void Add(SortedDictionary<string, long> into, string key, long value) =>
        into[key] = into.GetValueOrDefault(key) + value;

    public void Dispose()
    {
        foreach (var experience in _skills.Keys)
            experience.XpChanged -= XpChanged;
        _copy.Bank.Inventory.ItemQuantityChanged -= BankChanged;
        _copy.Bank.Wallet.CurrencyQuantityChanged -= WalletChanged;
        foreach (var (slot, handler) in _equipment)
            slot.QuantityChanged -= handler;
        foreach (var (slot, handler) in _toolbelt)
            slot.QuantityChanged -= handler;
        if (Dependencies.IsAvailable(Dependencies.RareEvents))
            UnsubscribeRareEvents();
        if (RecordsRisk)
            UnsubscribeRisk();
        if (Totals)
            UnsubscribeRunes();
        Counting?.Dispose();
    }

    /// <summary>
    /// The hits taken since the last <see cref="Reset"/>: the largest, the lowest HP after one, and the HP lost.
    /// </summary>
    internal sealed class Hits
    {
        /// <summary>The largest hit at its full size, before it is cut to the HP left; null with no hit.</summary>
        public long? MaxHit { get; private set; }

        /// <summary>The lowest HP right after a hit; null with no hit.</summary>
        public long? MinHp { get; private set; }

        /// <summary>The HP actually removed.</summary>
        public long Damage { get; private set; }

        /// <summary>The largest damage reflected back at the player, at its full size; null with none.</summary>
        public long? MaxReflect { get; private set; }

        public void Reflected(long hit)
        {
            if (MaxReflect is not { } max || hit > max)
                MaxReflect = hit;
        }

        public void Add(long hit, long hp, long removed)
        {
            if (MaxHit is not { } max || hit > max)
                MaxHit = hit;
            if (MinHp is not { } min || hp < min)
                MinHp = hp;
            Damage += removed;
        }

        public void Reset()
        {
            MaxHit = MinHp = MaxReflect = null;
            Damage = 0;
        }
    }
}
