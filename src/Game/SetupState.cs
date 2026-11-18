using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>A combat skill's level and the cap it can be set to.</summary>
internal sealed record SkillLevel(string Skill, int Level, int Cap);

/// <summary>
/// An equipment or toolbelt slot, what it holds (null when empty), and what the item needs that the setup doesn't
/// have (the game's text; empty when it can be worn).
/// </summary>
internal sealed record SlotItem(string Slot, string? Item, IReadOnlyList<Note> Unmet);

/// <summary>A prayer, whether it is on, and its unmet unlock requirements (the game's text).</summary>
internal sealed record PrayerState(string Prayer, bool On, IReadOnlyList<Note> Locked);

/// <summary>A rune the selected spell and imbuement cost per cast, with how many the bank holds.</summary>
internal sealed record RuneCost(string Item, int PerCast, long Held);

/// <summary>A prayer slot: unlocked or not, and the prayer in it (null when empty).</summary>
internal sealed record PrayerSlotState(string Slot, bool Unlocked, string? Prayer);

/// <summary>
/// What a change can be made to in a setup, as the game has it: the combat levels with their caps, each equipment
/// and combat toolbelt slot's item, the prayers and prayer slots, the selected food, the potion worn (and whether the build has combat
/// potions), the attack stance, and the spell and imbuement with their rune costs.
/// </summary>
internal sealed record SetupState(
    IReadOnlyList<SkillLevel> Levels,
    IReadOnlyList<SlotItem> Slots,
    IReadOnlyList<SlotItem> Toolbelt,
    IReadOnlyList<PrayerState> Prayers,
    string? Food,
    bool PotionsInBuild,
    string? Potion,
    string? Stance,
    IReadOnlyList<string> Stances,
    IReadOnlyList<PrayerSlotState> PrayerSlots,
    long PrayerPoints,
    string? Spell,
    string? Imbuement,
    IReadOnlyList<string> Elements,
    IReadOnlyList<RuneCost> RuneCosts
)
{
    /// <summary>
    /// Reads the state of <paramref name="combat"/> in <paramref name="game"/> (a copy, or the live game on the main
    /// thread).
    /// </summary>
    public static SetupState Read(MelvorGame game, CombatEntity combat)
    {
        var levels = new List<SkillLevel>();
        foreach (var id in Changes.CombatSkills)
        {
            if (game.LeveledEntities.FirstOrDefault(e => e.Id == id) is { } skill)
                levels.Add(new SkillLevel(id, skill.Experience.Level, skill.Experience.LevelCap));
        }
        var equipment = combat.Player.Equipment;
        var slots = equipment
            ._activeSlots.Values.OrderBy(s => s.GridPosition.Y)
            .ThenBy(s => s.GridPosition.X)
            .Select(s => new SlotItem(
                s.Slot.Id,
                s.Equipped?.Item.Id,
                s.Equipped?.Item.Capabilities.TryGet<EquipmentCapability>(out var worn) == true
                    ? Targets.Unmet(worn.Requirements)
                    : []
            ))
            .ToList();
        var toolbelt = combat
            .Toolbelt._activeSlots.Values.OrderBy(s => s.GridPosition.Y)
            .ThenBy(s => s.GridPosition.X)
            .Select(s => new SlotItem(
                s.Slot.Id,
                s.Equipped?.Item.Id,
                s.Equipped?.Item.Capabilities.TryGet<ToolbeltCapability>(out var held) == true
                    ? Targets.Unmet(held.Requirements)
                    : []
            ))
            .ToList();
        var prayers = combat
            .Prayer.ActivePrayers.Keys.Select(p => new PrayerState(
                p.Id,
                combat.Prayer.IsPrayerSelected(p),
                Targets.Unmet(p.UnlockRequirements)
            ))
            .ToList();
        var potion = combat
            .Toolbelt._activeSlots.Values.FirstOrDefault(s => s.Slot.FitsType.Id == Changes.CombatPotionSlot)
            ?.Equipped?.Item.Id;
        var player = combat.Player;
        var prayerSlots = combat
            .Prayer.ActiveSlots.Values.Select(s => new PrayerSlotState(
                s.Data.Id,
                s.UnlockRequirements.AreMet,
                s.Selected?.Data.Id
            ))
            .ToList();
        var points = game.Data.TryGetObject<GamesByMalcs.Melvor2.Data.Currencies.Currency>(
            PrayerPointsId,
            out var currency
        )
            ? combat.Prayer.Wallet.GetQuantity(currency)
            : 0;
        var magic = combat.MagicSpells;
        var runes = new Dictionary<GamesByMalcs.Melvor2.Data.Items.Item, int>();
        var casts = new List<GamesByMalcs.Melvor2.Components.MagicSpells.BaseMagicSpell?>
        {
            magic.CanUseSpells ? magic.SelectedSpell?.Data : null,
            magic.SelectedImbuement?.Data,
        };
        foreach (var cast in casts.OfType<GamesByMalcs.Melvor2.Components.MagicSpells.BaseMagicSpell>())
        {
            foreach (var cost in cast.RuneCosts)
                runes[cost.Item] = runes.GetValueOrDefault(cost.Item) + cost.Quantity;
        }
        var bank = game.Bank.Inventory;
        return new SetupState(
            levels,
            slots,
            toolbelt,
            prayers,
            combat.Food.SelectedFood?.Item.Id,
            Changes.PotionsInBuild(game),
            potion,
            player.Stats.AttackStance.Value?.Id,
            [.. player.Equipment.CurrentWeapon.Weapon.Type.Stances.Select(s => s.Id)],
            prayerSlots,
            points,
            magic.SelectedSpell?.Data.Id,
            magic.SelectedImbuement?.Data.Id,
            [.. magic.AvailableElements.Select(e => e.Id)],
            [.. runes.Select(r => new RuneCost(r.Key.Id, r.Value, bank.GetQuantity(r.Key)))]
        );
    }

    private const string PrayerPointsId = "MelvorBase:PrayerPoints";

    /// <summary>One line per part, in a fixed order, for replies and the live-game hash.</summary>
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>();
        lines.AddRange(
            Levels.Select(l => string.Create(CultureInfo.InvariantCulture, $"level {Ids.Brief(l.Skill)} {l.Level}"))
        );
        lines.AddRange(Slots.Select(s => $"slot {Ids.Brief(s.Slot)} {(s.Item is { } i ? Ids.Brief(i) : Change.None)}"));
        lines.AddRange(
            Toolbelt.Select(s => $"toolbelt {Ids.Brief(s.Slot)} {(s.Item is { } i ? Ids.Brief(i) : Change.None)}")
        );
        lines.Add($"stance {(Stance is { } st ? Ids.Brief(st) : "?")}");
        lines.Add($"spell {(Spell is { } sp ? Ids.Brief(sp) : Change.None)}");
        lines.Add($"imbuement {(Imbuement is { } im ? Ids.Brief(im) : Change.None)}");
        lines.Add($"food {(Food is { } f ? Ids.Brief(f) : Change.None)}");
        lines.AddRange(Prayers.Where(p => p.On).Select(p => $"prayer {Ids.Brief(p.Prayer)} on"));
        lines.Add($"potion {(Potion is { } p ? Ids.Brief(p) : Change.None)}");
        return lines;
    }

    /// <summary>The first 12 hex characters of the SHA-256 of <paramref name="lines"/>.</summary>
    public static string Hash(IEnumerable<string> lines) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))))[..12];
}
