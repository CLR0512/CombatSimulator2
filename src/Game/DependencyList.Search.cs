using System.Reflection;
using MelvorMods.CombatSimulator2.Game.Search;

namespace MelvorMods.CombatSimulator2.Game;

// What the gear search reads of the game: an item as its parts, what a stance and a target read, the sets and the
// other loadouts' gear.
internal static partial class Dependencies
{
    public const string GearSearch = "gear-search";

    // The entries kept in files of their own, so the main table stays under the length a file may have.
    private static IEnumerable<Dependency> Later =>
        [.. SetupPages, .. Search, .. SlayerMasterRows, .. SpoilerGuardRows];

    private static IEnumerable<Dependency> Search =>
        [
            .. T(
                GearSearch,
                "Components.Equipment.EquipmentCapability",
                "property:Type",
                "property:OccupiesSlots",
                "property:Requirements",
                "property:Stats",
                "property:Bonuses",
                "property:CanEquipQuantity",
                "property:ConsumesOn",
                "property:Attacks",
                "property:AmmoType"
            ),
            .. T(
                GearSearch,
                "Components.Equipment.WeaponCapability",
                "property:Type",
                "property:DamageType",
                "property:Elements",
                "property:RequiredAmmoType"
            ),
            .. T(GearSearch, "Components.Equipment.WeaponType", "property:Stances"),
            .. T(GearSearch, "Components.SlayerTasks.SlayerTasksComponent", "HasTaskForEnemy"),
            .. T(GearSearch, "Components.SlayerTasks.Conditions.FightingSlayerTaskCondition"),
            .. T(GearSearch, "Components.Equipment.EquipmentSetBonus", "property:Items", "property:Bonuses"),
            .. T(GearSearch, "Components.Equipment.SetBonusModifiers", "property:ItemCount", "property:Modifiers"),
            .. T(GearSearch, "Components.Equipment.EquipmentSlot", "property:FitsType", "property:UnlockRequirements"),
            .. T(GearSearch, "Components.Equipment.EquipmentComponent", "field:_activeSlots", "property:Loadouts"),
            .. T(GearSearch, "Components.Equipment.EquipmentLoadout", "property:Slots"),
            .. T(
                GearSearch,
                "Components.Equipment.EquipmentSlotLoadout",
                "property:Equipped",
                "property:Quantity",
                "property:IsOccupied"
            ),
            .. T(
                GearSearch,
                "Components.CombatStats.CombatStatValues",
                "property:Stats",
                "property:PowerRatings",
                "property:ProtectionRatings"
            ),
            .. T(GearSearch, "Components.CombatStats.CombatStat", "property:Category"),
            .. T(
                GearSearch,
                "Modifiers.ModifierValues",
                "property:Global",
                "property:Component",
                "property:CombatEffect"
            ),
            .. T(
                GearSearch,
                "Modifiers.ComponentModifierValue",
                "property:Stat",
                "property:Value",
                "property:Condition",
                "property:Scaling",
                "property:IsEnemy",
                "GetDescription"
            ),
            .. T(
                GearSearch,
                "Modifiers.GlobalModifierValue",
                "property:StatGroup",
                "property:Value",
                "property:Condition",
                "property:Scaling",
                "property:Description"
            ),
            .. T(GearSearch, "Modifiers.GlobalModifierDescription", "property:Kind"),
            .. T(GearSearch, "Stats.ModifiableStat", "property:Name", "property:StatsClassId", "property:Kind"),
            .. T(
                GearSearch,
                "Components.CombatEffects.CombatEffectModifierValue",
                "property:Modifier",
                "property:Chance",
                "property:Condition"
            ),
            .. T(
                GearSearch,
                "Components.CombatEffects.CombatEffectModifier",
                "property:Effect",
                "property:Trigger",
                "property:AppliesTo"
            ),
            .. T(GearSearch, "Components.CombatEffects.ConfiguredCombatEffect", "property:Data"),
            .. T(GearSearch, "Components.CombatEffects.CombatEffectTrigger", "property:Description"),
            .. T(GearSearch, "Entities.Combat.AttackChance", "property:Attack", "property:Chance"),
            .. T(
                GearSearch,
                "Entities.Combat.AttackStance",
                "property:CombatStyle",
                "property:AccuracyStat",
                "property:EvasionStat"
            ),
            .. T(
                GearSearch,
                "Entities.Combat.CombatStyle",
                "property:MaxHitFormula",
                "property:AttackIntervalStat",
                "property:MaxWardStat",
                "property:ArmourStat",
                "property:LifestealStat",
                "property:UsesSpells"
            ),
            .. T(GearSearch, "Entities.Combat.MaxHitFormula", "GetCombatStatDependencies"),
            .. T(GearSearch, "Entities.Combat.Enemy", "property:Stances"),
            .. T(GearSearch, "Entities.Combat.WeightedAttackStance", "property:Stance"),
            .. T(GearSearch, "Components.CombatAreas.Dungeon", "property:Enemies"),
            .. T(GearSearch, "Components.CombatAreas.DungeonEnemies", "property:Enemies"),
            .. T(GearSearch, "Components.Inventories.StackableInventory", "MoveStack"),
            .. T(GearSearch, "Components.CombatLoot.CombatDrops", "property:FixedItems", "property:ItemTables"),
            .. T(GearSearch, "Entities.Shop.ShopPurchase", "property:Provides"),
            .. T(GearSearch, "Entities.Shop.ShopPurchaseProvides", "property:Items"),
            .. T(GearSearch, "Components.ArtisanAction.ArtisanRecipe", "property:Products", "property:Category"),
            .. T(GearSearch, "Components.ItemUpgrading.ItemUpgrade", "property:UpgradedItem"),
            .. T(GearSearch, "Entities.Bank.BankInventoryComponent", "property:InventoryTabs"),
            // A copy put back sets the totals left with nothing in them to zero (ModifierTotals)
            .. T(GearSearch, "Modifiers.ModifierSystem", "property:ModifiableStatTables", "property:StatGroupTables"),
            .. T(GearSearch, "Modifiers.ModifierTable", "event:EntryAdded"),
            .. T(GearSearch, "Modifiers.ModifierQuery", "property:Entries", "property:Total", "AdjustTotal"),
            // Each set's stats against the target (TargetStatsKey)
            .. T(
                GearSearch,
                "Components.Characters.CharacterStats",
                "property:MinHit",
                "property:MaxHit",
                "property:AccuracyRating",
                "property:AttackInterval",
                "property:MaxHitpoints",
                "property:MaxWard",
                "property:Armour",
                "property:Lifesteal",
                "property:DisableLifesteal",
                "property:CriticalHitChance",
                "property:CriticalHitMultiplier",
                "property:PercentAttackReflect",
                "property:PercentDamageTaken",
                "property:PercentDamageDealt",
                "property:AttackDamageNegationChance",
                "property:DoubleWardDamageChance",
                "property:EvasionRatings",
                "property:PowerRatings",
                "property:ProtectionRatings",
                "property:Element"
            ),
            .. T(
                GearSearch,
                "Entities.Combat.TargetCharacterStats",
                "property:HitChance",
                "property:FlatHitChance",
                "property:PercentDamageTakenFrom",
                "property:PercentDamageTakenFromAttacks",
                "property:ImmunityFrom",
                "property:DefenderResistances"
            ),
        ];

    /// <summary>
    /// The kinds the game has that <see cref="KnownParts"/> lacks, as <c>effect kind: StunEffect</c>: every concrete
    /// class under a closed set's base that isn't one of its known names. An item carrying one is opaque to the
    /// search, so nothing turns off; it is said so that the kind gets a rule.
    /// </summary>
    public static IReadOnlyList<string> UnknownKinds()
    {
        var assembly = typeof(GamesByMalcs.Melvor2.Game).Assembly;
        var unknown = new List<string>();
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = [.. e.Types.OfType<Type>()];
        }
        foreach (var set in KnownParts.ClosedSets)
        {
            if (assembly.GetType(Ns + set.Base) is not { } root)
            {
                unknown.Add($"{set.What}: {set.Base} is gone");
                continue;
            }
            unknown.AddRange(
                types
                    .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && root.IsAssignableFrom(t))
                    .Where(t => !t.Name.StartsWith("Placeholder", StringComparison.Ordinal))
                    .Where(t => !set.Known.Contains(t.Name, StringComparer.Ordinal))
                    .OrderBy(t => t.Name, StringComparer.Ordinal)
                    .Select(t => $"{set.What}: {t.Name}")
            );
        }
        return unknown;
    }
}
