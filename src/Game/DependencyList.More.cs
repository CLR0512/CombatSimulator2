namespace MelvorMods.CombatSimulator2.Game;

// The catalogue's later entries, in a file of their own for its length.
internal static partial class Dependencies
{
    public const string FightCounts = "fight-counts";
    public const string Totals = "used-totals";
    public const string Quiet = "panel-quiet-notifications";

    private static IEnumerable<Dependency> More =>
        [
            // Both fighters' attacks, the enemy's losses and heals, the player's losses by cause, and the fight's start
            .. T(
                FightCounts,
                "Components.Characters.CharacterComponent",
                "event:Attacked",
                "property:TargetStats",
                "property:Ward",
                "property:Health"
            ),
            .. T(FightCounts, "Entities.Combat.AttackEventArgs", "property:WasHit", "property:WardDamage"),
            .. T(
                FightCounts,
                "Components.Characters.ICharacterHealth",
                "event:Damaged",
                "event:Healed",
                "property:Current",
                "property:Max"
            ),
            .. T(FightCounts, "Components.Characters.HealthEventArgs", "property:Amount", "property:Cause"),
            .. T(
                FightCounts,
                "Components.Characters.HealthChangeCause",
                "field:Attack",
                "field:CombatEffect",
                "field:Reflect"
            ),
            .. T(
                FightCounts,
                "Components.Characters.ModifiedDamage",
                "property:ResistanceReduction",
                "property:WardReduction",
                "property:ArmourReduction"
            ),
            .. T(FightCounts, "Components.Characters.EnemyComponent", "event:Spawned"),
            .. T(FightCounts, "Components.Characters.PlayerComponent", "property:GlobalHealth"),
            .. T(FightCounts, "Components.Characters.CharacterHealth", "event:Damaged"),
            .. T(FightCounts, "Components.Characters.HealthDamagedEventArgs", "property:Damage"),
            .. T(FightCounts, "Components.Characters.ModifiedDamage", "property:Modified"),
            .. T(FightCounts, "Entities.Combat.TargetCharacterStats", "property:ImmunityFrom"),
            .. T(Changes, "Components.Experience.IExperienceTable", "GetXpForLevel"),
            // What a copy's reset empties before a component reads its saved state again (Rereads)
            .. T(
                Changes,
                "Components.Prayers.PrayerComponent",
                "property:Loadouts",
                "property:FavouritePrayers",
                "property:HiddenPrayers"
            ),
            .. T(Changes, "Components.Toolbelt.ToolbeltComponent", "property:Loadouts"),
            .. T(
                Changes,
                "Components.MagicSpells.MagicSpellsComponent",
                "property:Loadouts",
                "field:_defaultSpellsForElement"
            ),
            .. T(Changes, "Components.Loadouts.LoadoutComponent", "field:_loadoutDisplayOrder"),
            .. T(Changes, "Components.SlayerTasks.SlayerTasksComponent", "field:_mastersWithTask"),
            .. T(Changes, "Components.SlayerTasks.ActiveSlayerMaster", "field:_currentTask", "property:CurrentTask"),
            // The elements a side's own weapon allows, for the spell picker
            .. T(Changes, "Components.Equipment.WeaponCapability", "property:Elements"),
            .. T(Changes, "Components.Equipment.EquipmentComponent", "property:DefaultWeapon"),
            // The game's notifications, drawn behind while the window is open when the player asks
            .. T(Quiet, "Notifications.NotificationManager"),
            .. T(Quiet, "Notifications.Notification", "ResetAutoDismiss", "property:State"),
            // Reflected damage among the hits risk records
            .. T(Risk, "Components.Characters.HealthEventArgs", "property:Cause"),
            .. T(Risk, "Components.Characters.HealthChangeCause", "field:Reflect"),
            // Food, potions and runes used, counted together, and what the bones dropped could give if buried
            .. T(Totals, "Components.CombatFood.FoodCapability"),
            .. T(Totals, "Components.MagicSpells.MagicSpellsComponent", "property:Inventory"),
            .. T(Totals, "Components.Inventories.TrackableInventory", "event:ItemAdded"),
            .. T(Totals, "Components.Inventories.ItemAddedEventArgs", "property:Quantity"),
            .. T(Totals, "Components.ItemBurying.BuryCapability", "property:CurrencyGain"),
            .. T(Totals, "Components.ItemBurying.ItemBuryingComponent", "GetItemStats"),
            .. T(Totals, "Components.ItemBurying.ItemBuryingStats", "property:RewardPerBury"),
            .. T(Totals, "Entities.Bank.BankEntity", "property:ItemBurying"),
            .. T(Totals, "Entities.Combat.CombatEntity", "property:MagicSpells"),
        ];
}
