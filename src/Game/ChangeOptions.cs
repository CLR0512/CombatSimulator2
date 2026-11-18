using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Components.CombatStats;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.MagicSpells;
using GamesByMalcs.Melvor2.Components.Prayers;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// One choice a change can be set to, as the editor's picker lists it: its id and name, the element it needs (for
/// spells), whether the live character holds or wears it, and its unmet requirements in the live game.
/// </summary>
internal sealed record ChangeOption(
    string Id,
    string Name,
    string? Detail,
    bool Owned,
    bool WornInA,
    IReadOnlyList<Note> Unmet
);

/// <summary>An equipment slot where the game's grid puts it.</summary>
internal sealed record GridSlot(string Slot, int Column, int Row);

/// <summary>A combat toolbelt slot: its id, the type it fits, and whether the character has unlocked it.</summary>
internal sealed record ToolbeltSlotInfo(string Slot, string FitsType, bool Unlocked, int Column);

/// <summary>
/// What the what-if editor shows from the live game's data, read on the main thread: the choices for each kind of
/// change, the equipment grid and toolbelt as the game lays them out, and icons for any id it shows. Read only.
/// </summary>
internal static class ChangeOptions
{
    /// <summary>
    /// Whether an item may be offered to a side that doesn't have it. The game's data carries items no player can get
    /// (a developer's test item among the toolbelt's), and its completion log leaves exactly those out, so the
    /// pickers and searches do too; one the character somehow has is still shown.
    /// </summary>
    public static bool Offered(Item item, bool had) => had || !item.IgnoreCompletion;

    /// <summary>
    /// The choices for <paramref name="kind"/> (and, for gear, the slot <paramref name="subject"/>) in the game's
    /// order. Prayers, spells and stances put the side's own first; items keep the game's order
    /// whether owned or not, so an unowned item the picker lets in sits where it would if it were owned.
    /// </summary>
    public static IReadOnlyList<ChangeOption> For(
        MelvorGame game,
        ChangeKind kind,
        string? subject,
        IReadOnlyCollection<string>? gear = null
    )
    {
        ArgumentNullException.ThrowIfNull(game);
        var combat = LiveGame.Combat(game);
        var bank = game.Bank.Inventory;
        var worn = combat.Player.Equipment.EquippedItems.ToHashSet();
        var options = new List<ChangeOption>();
        var items = kind is ChangeKind.Equip or ChangeKind.Toolbelt or ChangeKind.Food;
        switch (kind)
        {
            case ChangeKind.Equip:
                if (!game.Data.TryGetObject<EquipmentSlot>(subject ?? "", out var slot))
                    break;
                foreach (var item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
                {
                    if (
                        !item.Capabilities.TryGet<EquipmentCapability>(out var equipment)
                        || equipment.Type != slot.FitsType
                        || !Offered(item, bank.GetQuantity(item) > 0 || worn.Contains(item))
                    )
                        continue;
                    options.Add(
                        Option(
                            item.Id,
                            LiveGame.NameOf(game, item.Id),
                            null,
                            bank.GetQuantity(item) > 0,
                            worn.Contains(item),
                            equipment.Requirements
                        )
                    );
                }
                break;
            case ChangeKind.Toolbelt:
                if (!game.Data.TryGetObject<ToolbeltSlot>(subject ?? "", out var toolbeltSlot))
                    break;
                foreach (var item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
                {
                    if (
                        !item.Capabilities.TryGet<ToolbeltCapability>(out var fits)
                        || fits.FitsInSlot != toolbeltSlot.FitsType
                        || !Offered(item, bank.GetQuantity(item) > 0 || combat.Toolbelt.IsItemEquipped(item))
                    )
                        continue;
                    options.Add(
                        Option(
                            item.Id,
                            LiveGame.NameOf(game, item.Id),
                            null,
                            bank.GetQuantity(item) > 0,
                            combat.Toolbelt.IsItemEquipped(item),
                            fits.Requirements
                        )
                    );
                }
                break;
            case ChangeKind.Food:
                foreach (var item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
                {
                    if (item.Capabilities.TryGet<FoodCapability>(out _))
                        options.Add(
                            Option(
                                item.Id,
                                LiveGame.NameOf(game, item.Id),
                                null,
                                bank.GetQuantity(item) > 0,
                                combat.Food.SelectedFood?.Item == item,
                                null
                            )
                        );
                }
                break;
            case ChangeKind.Prayer:
                foreach (var prayer in combat.Prayer.ActivePrayers.Keys)
                    options.Add(
                        Option(
                            prayer.Id,
                            prayer.DisplayName.Text,
                            null,
                            false,
                            combat.Prayer.IsPrayerSelected(prayer),
                            prayer.UnlockRequirements
                        )
                    );
                break;
            case ChangeKind.Spell:
                var spellElements = Elements(game, combat, gear);
                foreach (var spell in combat.MagicSpells.ActiveSpells.Keys)
                    options.Add(Spell(spell, spellElements, combat.MagicSpells.SelectedSpell?.Data == spell));
                break;
            case ChangeKind.Imbuement:
                var imbuementElements = Elements(game, combat, gear);
                foreach (var imbuement in combat.MagicSpells.ActiveImbuements.Keys)
                    options.Add(
                        Spell(imbuement, imbuementElements, combat.MagicSpells.SelectedImbuement?.Data == imbuement)
                    );
                break;
            case ChangeKind.Stance:
                foreach (var stance in combat.Player.Equipment.CurrentWeapon.Weapon.Type.Stances)
                    options.Add(Option(stance.Id, stance.DisplayName.Text, null, false, false, null));
                break;
            default:
                break;
        }
        return
        [
            .. options
                .Select((o, at) => (o, at))
                .OrderBy(p => items || p.o.WornInA ? 0 : 1)
                .ThenBy(p => p.at)
                .Select(p => p.o),
        ];
    }

    private static ChangeOption Option(
        string id,
        string name,
        string? detail,
        bool owned,
        bool worn,
        ConditionGroup? requirements
    )
    {
        var unmet = requirements == null ? [] : Targets.Unmet(requirements);
        return new ChangeOption(id, name, detail, owned, worn, unmet);
    }

    // The elements a side's gear lets it cast, as the game sets them from the weapon: the side's own weapon when its
    // gear is given (a change may have put one there that the character doesn't wear), the game's bare-handed weapon
    // when it has none; the live character's otherwise.
    private static System.Collections.ObjectModel.ReadOnlyCollection<Element> Elements(
        MelvorGame game,
        CombatEntity combat,
        IReadOnlyCollection<string>? gear
    )
    {
        if (gear == null)
            return combat.MagicSpells.AvailableElements;
        foreach (var id in gear)
            if (
                game.Data.TryGetObject<Item>(id, out var item)
                && item.Capabilities.TryGet<WeaponCapability>(out var weapon)
            )
                return weapon.Elements;
        return combat.Player.Equipment.DefaultWeapon.Weapon.Elements;
    }

    // A spell or imbuement: its element in the detail, and "no <element> element" when the gear lacks it.
    private static ChangeOption Spell(
        BaseMagicSpell spell,
        System.Collections.ObjectModel.ReadOnlyCollection<Element> elements,
        bool selected
    )
    {
        var option = Option(
            spell.Id,
            spell.DisplayName.Text,
            spell.Element.DisplayName.Text,
            false,
            selected,
            spell.UnlockRequirements
        );
        if (elements.Contains(spell.Element))
            return option;
        return option with
        {
            Unmet = [.. option.Unmet, Note.Of("weapon can't cast {0}", spell.Element.DisplayName.Text)],
        };
    }

    /// <summary>The live character's equipment slots, where the game's grid puts them.</summary>
    public static IReadOnlyList<GridSlot> Grid(MelvorGame game)
    {
        var equipment = LiveGame.Combat(game).Player.Equipment;
        var slots = equipment._activeSlots.Values.ToList();
        if (slots.Count == 0)
            return [];
        var minX = slots.Min(s => s.GridPosition.X);
        var minY = slots.Min(s => s.GridPosition.Y);
        return [.. slots.Select(s => new GridSlot(s.Slot.Id, s.GridPosition.X - minX, s.GridPosition.Y - minY))];
    }

    /// <summary>The combat toolbelt's slots in the game's order.</summary>
    public static IReadOnlyList<ToolbeltSlotInfo> Toolbelt(MelvorGame game)
    {
        var toolbelt = LiveGame.Combat(game).Toolbelt;
        return
        [
            .. toolbelt
                ._activeSlots.Values.OrderBy(s => s.GridPosition.Y)
                .ThenBy(s => s.GridPosition.X)
                .Select(s => new ToolbeltSlotInfo(
                    s.Slot.Id,
                    s.Slot.FitsType.Id,
                    s.Slot.UnlockRequirements.AreMet,
                    s.GridPosition.X
                )),
        ];
    }

    /// <summary>
    /// The game's icon for an id the editor shows: an item, slot, prayer, stance, spell, imbuement, slot type,
    /// currency or skill.
    /// </summary>
    public static Texture2D? Icon(MelvorGame game, string id)
    {
        ArgumentNullException.ThrowIfNull(game);
        var data = game.Data;
        if (data.TryGetObject<Item>(id, out _))
            return Art.Item(game, id);
        if (data.TryGetObject<EquipmentSlot>(id, out var slot))
            return slot.FitsType.Icon;
        if (data.TryGetObject<Prayer>(id, out var prayer))
            return prayer.Icon;
        if (data.TryGetObject<AttackStance>(id, out var stance))
            return stance.Icon;
        if (data.TryGetObject<MagicSpell>(id, out var spell))
            return spell.Icon;
        if (data.TryGetObject<Imbuement>(id, out var imbuement))
            return imbuement.Icon;
        if (data.TryGetObject<ToolbeltSlotType>(id, out var type))
            return type.Icon;
        if (data.TryGetObject<GamesByMalcs.Melvor2.Entities.Shop.ShopPurchase>(id, out var purchase))
            // Most purchases have a stand-in picture: those show their shop category's icon.
            return IsStandIn(purchase.Icon) ? Icon(game, purchase.Category.Id) : purchase.Icon;
        if (data.TryGetObject<GamesByMalcs.Melvor2.Data.Currencies.Currency>(id, out _))
            return Art.Currency(game, id);
        if (data.TryGetObject<GamesByMalcs.Melvor2.Entities.Herblore.HerblorePotionGroup>(id, out var potion))
            // Some potions have a stand-in picture: those show their first potency's.
            return
                IsStandIn(potion.Icon)
                && data.GetDataRegistryOutput<GamesByMalcs.Melvor2.Entities.Herblore.HerbloreRecipe>()
                    .GetAllObjects()
                    .FirstOrDefault(r => r.PotionGroup == potion && r.Products.Count > 0)
                    is { } recipe
                ? Art.Item(game, recipe.Products[0].Item.Id)
                : potion.Icon;
        if (data.TryGetObject<GamesByMalcs.Melvor2.Components.Pets.Pet>(id, out var pet))
            return pet.Icon;
        if (data.TryGetObject<GamesByMalcs.Melvor2.Entities.Firemaking.FiremakingBonfire>(id, out var bonfire))
            // A tier with a stand-in picture shows Firemaking's.
            return IsStandIn(bonfire.Icon) ? Art.Skill(game, BonfireChange.FiremakingId) : bonfire.Icon;
        if (data.TryGetObject<GamesByMalcs.Melvor2.Entities.Agility.AgilityTechnique>(id, out var technique))
            return technique.Icon;
        return game.Entities.TryGetObjectById(id, out _) ? Art.Skill(game, id) : null;
    }

    // An item's stats, each as the game formats it, with the change from the item it replaces.
    private static void Stats(List<Note> lines, CombatStatValues now, CombatStatValues? was)
    {
        // Green for the better, red for the worse: more is better, except for an interval, where shorter is.
        static string Change(double by, Func<double, string> format, bool lessIsBetter = false)
        {
            if (by == 0)
                return "";
            var text = "(" + (by > 0 ? "+" : "−") + format(Math.Abs(by)) + ")";
            return " " + (by > 0 != lessIsBetter ? Marks.Good(text) : Marks.Bad(text));
        }
        static string Whole(double value) => value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
        var stats = now.Stats.Select(s => s.Stat).Concat(was?.Stats.Select(s => s.Stat) ?? []).Distinct();
        foreach (var stat in stats.OrderBy(s => s.Category.Id, StringComparer.Ordinal).ThenBy(s => s.DisplayPriority))
        {
            var value = now.GetCombatStatTotal(stat);
            var by = was == null ? 0 : value - was.GetCombatStatTotal(stat);
            string Format(double v) => stat.NumberFormat.Format(v);
            if (value != 0 || by != 0)
                lines.Add(
                    Note.Text(
                        stat.DisplayName.Text
                            + ": "
                            + Format(value)
                            + Change(by, Format, stat.Id.EndsWith("interval", StringComparison.OrdinalIgnoreCase))
                    )
                );
        }
        var power = now.PowerRatings.Concat(was?.PowerRatings ?? []).Select(r => r.DamageType).Distinct();
        foreach (var type in power)
        {
            var value = now.GetPowerRatingTotal(type);
            var by = was == null ? 0 : value - was.GetPowerRatingTotal(type);
            if (value != 0 || by != 0)
                lines.Add(Note.Of("Power ({0}): {1}", type.DisplayName.Text, Whole(value) + Change(by, Whole)));
        }
        var protection = now
            .ProtectionRatings.Concat(was?.ProtectionRatings ?? [])
            .Select(r => r.DamageType)
            .Distinct();
        foreach (var type in protection)
        {
            var value = now.GetProtectionRatingTotal(type);
            var by = was == null ? 0 : value - was.GetProtectionRatingTotal(type);
            if (value != 0 || by != 0)
                lines.Add(Note.Of("Protection ({0}): {1}", type.DisplayName.Text, Whole(value) + Change(by, Whole)));
        }
    }

    /// <summary>Whether <paramref name="icon"/> is one of the game's stand-ins for art it doesn't have yet.</summary>
    public static bool IsStandIn(Texture2D? icon) =>
        icon?.ResourcePath is not { Length: > 0 } path
        || path.EndsWith("/question.png", StringComparison.Ordinal)
        || path.EndsWith("/missing_artwork.png", StringComparison.Ordinal);

    // The same, for the live game: the panel never names game types.

    /// <summary>The choices for <paramref name="kind"/> in the live game; none without one.</summary>
    /// <summary>
    /// The choices from the live game; <paramref name="gear"/> is the side's worn items, whose weapon decides which
    /// spells and imbuements its elements allow.
    /// </summary>
    public static IReadOnlyList<ChangeOption> For(
        ChangeKind kind,
        string? subject,
        IReadOnlyCollection<string>? gear = null
    ) => LiveGame.Find() is { } game ? For(game, kind, subject, gear) : [];

    public static IReadOnlyList<GridSlot> Grid() => LiveGame.Find() is { } game ? Grid(game) : [];

    public static IReadOnlyList<ToolbeltSlotInfo> Toolbelt() => LiveGame.Find() is { } game ? Toolbelt(game) : [];

    public static Texture2D? Icon(string id) => LiveGame.Find() is { } game ? Icon(game, id) : null;

    /// <summary>The pets' picture: the build's first pet, as the game has no picture for pets as a whole.</summary>
    public static Texture2D? PetsIcon() =>
        LiveGame
            .Find()
            ?.Data.GetDataRegistryOutput<GamesByMalcs.Melvor2.Components.Pets.Pet>()
            .GetAllObjects()
            .FirstOrDefault()
            ?.Icon;

    /// <summary>
    /// What the live game says about a choice, a line each: an item's description, what food heals, equipment's
    /// stats (with the change from <paramref name="worn"/>, the item it would replace, in brackets), and what an
    /// item or prayer gives. Read when asked, for the one choice the pointer is on.
    /// </summary>
    public static IReadOnlyList<Note> About(string id, string? worn = null)
    {
        var lines = new List<Note>();
        if (LiveGame.Find() is not { } game)
            return lines;
        GamesByMalcs.Melvor2.Modifiers.ModifierValues? gives = null;
        var group = ModifierGroup.Equipment;
        if (game.Data.TryGetObject<Item>(id, out var item))
        {
            if (item.Description?.Text is { } text && Targets.Plain(text) is { Length: > 0 } plain)
                lines.Add(Note.Text(plain));
            if (item.Capabilities.TryGet<FoodCapability>(out var food))
                lines.Add(Note.Of("Heals {0}", food.BaseHealing));
            if (item.Capabilities.TryGet<EquipmentCapability>(out var equipment))
            {
                gives = equipment.Bonuses;
                var replaced =
                    worn != null
                    && worn != id
                    && game.Data.TryGetObject<Item>(worn, out var other)
                    && other.Capabilities.TryGet<EquipmentCapability>(out var before)
                        ? before.Stats
                        : null;
                if (replaced != null)
                    lines.Add(Note.Of("In brackets: change from {0}", LiveGame.NameOf(game, worn!)));
                Stats(lines, equipment.Stats, replaced);
            }
            else if (item.Capabilities.TryGet<ToolbeltCapability>(out var toolbelt))
                (gives, group) = (toolbelt.Bonuses, ModifierGroup.Toolbelt);
        }
        else if (game.Data.TryGetObject<Prayer>(id, out var prayer))
            (gives, group) = (prayer.Bonuses, ModifierGroup.Prayer);
        if (gives != null)
            foreach (var line in SetupSheets.Describe(group, id, "", gives))
                lines.Add(Note.Text(line.Description));
        return lines;
    }
}
