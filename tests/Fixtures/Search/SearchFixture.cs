using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>
/// A small bank for the search's tests: twelve slots and about forty items, a few of each kind the rules treat
/// differently. The character wears the Stone set with a Stone Sword and fights in Slash.
/// </summary>
internal static class SearchFixture
{
    public const string Attack = "attack";
    public const string Strength = "strength";
    public const string Defence = "defence";
    public const string Magic = "magic";

    public static string Id(string name) => Ids.Full(name);

    public static readonly IReadOnlyList<GearSlot> Slots =
    [
        Slot("Weapon"),
        Slot("Offhand"),
        Slot("Ammo"),
        Slot("Helmet"),
        Slot("Platebody"),
        Slot("Platelegs"),
        Slot("Boots"),
        Slot("Gloves"),
        Slot("Cape"),
        Slot("Amulet"),
        new(Id("Ring1"), "Ring"),
        new(Id("Ring2"), "Ring"),
    ];

    private static readonly WeaponPart Sword = new("Sword", "Melee", [Id("Stab"), Id("Slash"), Id("Block")]);
    private static readonly WeaponPart Axe = new("Axe", "Melee", [Id("Slash"), Id("Block")]);
    private static readonly WeaponPart Bow = new("Bow", "Ranged", [Id("Accurate"), Id("Rapid")], AmmoType: "Arrow");

    /// <summary>The catalogue as the character stands, with <paramref name="change"/> applied to its items.</summary>
    public static GearCatalogue Catalogue(Func<GearItem, GearItem>? change = null, bool targetAttacks = true)
    {
        var items = Items().Select(i => change?.Invoke(i) ?? i).ToList();
        return new GearCatalogue
        {
            Slots = Slots,
            Items = items,
            SetBonuses =
            [
                new GearSetBonus(
                    Id("GuardSet"),
                    "Guard set",
                    [Id("GuardHelmet"), Id("GuardBody"), Id("GuardLegs"), Id("GuardBoots")],
                    new Dictionary<int, IReadOnlyList<PartModifier>>
                    {
                        [2] = [new("DefencePercent", null, 5, Direction.Good, PartGroup.Defence)],
                        [4] =
                        [
                            new("DefencePercent", null, 10, Direction.Good, PartGroup.Defence),
                            new("StrengthPercent", null, 5, Direction.Good, PartGroup.Fight),
                        ],
                    }
                ),
            ],
            Current = items.Where(i => i.Worn != null).ToDictionary(i => i.Worn!, i => i.Id),
            Stance = Id("Slash"),
            TargetAttacks = targetAttacks,
            StatGroups = new Dictionary<string, PartGroup>
            {
                [Attack] = PartGroup.Fight,
                [Strength] = PartGroup.Fight,
                [Magic] = PartGroup.Fight,
                [Defence] = PartGroup.Defence,
            },
            Levels = new Dictionary<string, int> { [Id("Attack")] = 40, [Id("Defence")] = 40 },
            Build = "test-build",
        };
    }

    private static List<GearItem> Items() =>
        [
            // Weapons: three swords in a line, an axe that trades accuracy for strength, a two-hander and a bow.
            Item("StoneSword", "Weapon", (Attack, 5), (Strength, 5)) with
            {
                Weapon = Sword,
                Worn = Id("Weapon"),
            },
            Item("IronSword", "Weapon", (Attack, 10), (Strength, 10)) with
            {
                Weapon = Sword,
            },
            Item("SteelSword", "Weapon", (Attack, 15), (Strength, 15)) with
            {
                Weapon = Sword,
            },
            Item("IronBattleaxe", "Weapon", (Attack, 8), (Strength, 14)) with
            {
                Weapon = Axe,
            },
            Item("Iron2HSword", "Weapon", (Attack, 14), (Strength, 20)) with
            {
                Weapon = new WeaponPart("Sword2H", "Melee", [Id("Slash"), Id("Block")]),
                Occupies = [Id("Offhand")],
            },
            Item("IronBow", "Weapon", (Attack, 12), (Strength, 8)) with
            {
                Weapon = Bow,
                Occupies = [Id("Offhand")],
            },
            Item("IronArrows", "Ammo", (Strength, 4)) with
            {
                AmmoType = "Arrow",
                Stackable = true,
                Owned = 500,
                ConsumedOn = ["Attack"],
            },
            Item("SteelArrows", "Ammo", (Strength, 8)) with
            {
                AmmoType = "Arrow",
                Stackable = true,
                Owned = 200,
                ConsumedOn = ["Attack"],
            },
            Item("StoneShield", "Offhand", (Defence, 4)) with
            {
                Worn = Id("Offhand"),
            },
            Item("IronShield", "Offhand", (Defence, 8)),
            Item("SteelShield", "Offhand", (Defence, 12)),
            Item("StoneHelmet", "Helmet", (Defence, 3)) with
            {
                Worn = Id("Helmet"),
            },
            Item("IronHelmet", "Helmet", (Defence, 6)),
            Item("SteelHelmet", "Helmet", (Defence, 9)),
            Item("MageHat", "Helmet", (Defence, 1), (Magic, 6)),
            Item("StonePlatebody", "Platebody", (Defence, 8)) with
            {
                Worn = Id("Platebody"),
            },
            Item("IronPlatebody", "Platebody", (Defence, 16)),
            Item("SteelPlatebody", "Platebody", (Defence, 24)),
            Item("MithrilPlatebody", "Platebody", (Defence, 32)) with
            {
                Unmet = [Note.Text("Defence level 50")],
            },
            Item("StonePlatelegs", "Platelegs", (Defence, 6)) with
            {
                Worn = Id("Platelegs"),
            },
            Item("IronPlatelegs", "Platelegs", (Defence, 12)),
            Item("StoneBoots", "Boots", (Defence, 2)) with
            {
                Worn = Id("Boots"),
            },
            Item("IronBoots", "Boots", (Defence, 4)),
            Item("IronGloves", "Gloves", (Defence, 3), (Strength, 1)),
            Item("PlainCape", "Cape", (Defence, 1)),
            Item("ThornCape", "Cape", (Defence, 1)) with
            {
                Effects = [new ItemEffect("Reflect", "Hit")],
            },
            Item("AmuletOfAccuracy", "Amulet") with
            {
                Bonuses = [new("AccuracyPercent", null, 5, Direction.Good, PartGroup.Fight)],
            },
            Item("AmuletOfLearning", "Amulet") with
            {
                Bonuses = [new("XpPercent", null, 5, Direction.Good, PartGroup.Xp)],
            },
            Item("OddAmulet", "Amulet", (Strength, 1)) with
            {
                Opaque = true,
                OpaqueReason = "effect kind Odd",
            },
            // Rings: two Gold Rings can be worn at once; one Iron Ring is worse than gold in every stat.
            Item("GoldRing", "Ring", (Strength, 2)) with
            {
                Owned = 2,
            },
            Item("SilverRing", "Ring", (Attack, 2)),
            Item("IronRing", "Ring", (Strength, 1)),
            Guard("GuardHelmet", "Helmet", 5),
            Guard("GuardBody", "Platebody", 14),
            Guard("GuardLegs", "Platelegs", 10),
            Guard("GuardBoots", "Boots", 3),
            // Not owned: two that could be worn now, one that couldn't.
            Item("RuneSword", "Weapon", (Attack, 30), (Strength, 30)) with
            {
                Weapon = Sword,
                Owned = 0,
            },
            Item("RuneShield", "Offhand", (Defence, 30)) with
            {
                Owned = 0,
            },
            Item("DragonHelmet", "Helmet", (Defence, 40)) with
            {
                Owned = 0,
                Unmet = [Note.Text("Defence level 60")],
            },
        ];

    private static GearSlot Slot(string name) => new(Id(name), name);

    private static GearItem Item(string name, string slotType, params (string Stat, double Value)[] stats) =>
        new(Id(name), name, slotType) { Owned = 1, Stats = stats.ToDictionary(s => s.Stat, s => s.Value) };

    private static GearItem Guard(string name, string slotType, double defence) =>
        Item(name, slotType, (Defence, defence)) with
        {
            Sets = [Id("GuardSet")],
        };
}
