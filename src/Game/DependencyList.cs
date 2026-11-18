namespace MelvorMods.CombatSimulator2.Game;

// The catalogue: every game type, member, resource and data id CS2 depends on, by feature.
internal static partial class Dependencies
{
    // Features: what turns off when an entry is missing.
    public const string Engine = "engine";
    public const string Snapshot = "snapshot";
    public const string Guards = "guards";
    public const string Tally = "tally";
    public const string Adjust = "adjustments";
    public const string Targets = "targets";
    public const string Loadouts = "loadouts";
    public const string RareEvents = "rare-events";
    public const string CurrentTarget = "panel-current-target";
    public const string Values = "value";
    public const string Fingerprint = "fingerprint";
    public const string MainSkill = "main-skill";
    public const string Art = "art";
    public const string Gear = "panel-gear";
    public const string Changes = "changes";
    public const string Risk = "risk";
    public const string SetupSheet = "setup-sheet";
    public const string Drops = "drops";
    public const string Motion = "panel-motion";

    private static IEnumerable<Dependency> T(string feature, string type, params string[] members)
    {
        yield return new("type", type, feature);
        foreach (var member in members)
        {
            var colon = member.IndexOf(':', StringComparison.Ordinal);
            yield return colon < 0
                ? new("method", $"{type}.{member}", feature)
                : new(member[..colon], $"{type}.{member[(colon + 1)..]}", feature);
        }
    }

    private static Dependency D(string feature, string registry, string id) => new("data", $"{registry} {id}", feature);

    public static readonly IReadOnlyList<Dependency> List =
    [
        .. T(
            Snapshot,
            "Game",
            "WriteTo",
            "property:Data",
            "property:Entities",
            "property:IsSaveLoaded",
            "property:CharacterName",
            "property:ActionManager"
        ),
        .. T(Snapshot, "GameMenus.GameView", "property:Game"),
        .. T(Snapshot, "Saves.SaveWriter", "ctor:", "Flush"),
        .. T(Snapshot, "Saves.SaveManager", "field:CurrentSaveVersion"),
        .. T(Snapshot, "Core.Constants", "property:GameVersion", "property:GameSubversion", "property:TickInterval"),
        .. T(Snapshot, "Components.Actions.ActiveActionManager", "property:ActiveActions"),
        .. T(Snapshot, "Data.DataRegistry`1", "GetObjectById", "TryGetObjectById", "GetAllObjects"),
        .. T(Snapshot, "Data.DataManager", "TryGetObject", "GetObject"),
        .. T(Snapshot, "Entities.GameEntity", "property:Id", "property:DisplayName", "property:Components"),
        .. T(Snapshot, "Data.INamedObject", "property:DisplayName"),
        .. T(Snapshot, "Localization.ILocalizedString", "property:Text"),
        .. T(
            CurrentTarget,
            "Components.CombatAreas.CombatAreasComponent",
            "property:CurrentArea",
            "property:SelectedEnemy"
        ),
        .. T(CurrentTarget, "Components.CombatAreas.ActiveCombatArea", "property:Area"),
        .. T(
            Loadouts,
            "Components.Loadouts.LoadoutComponent",
            "property:LoadoutDisplayOrder",
            "property:ActiveLoadouts",
            "SwapToLoadout"
        ),
        .. T(
            Loadouts,
            "Components.Loadouts.ActiveLoadoutSlot",
            "property:Data",
            "property:Name",
            "property:IsUnlocked"
        ),
        .. T(Loadouts, "Components.Loadouts.LoadoutSlot", "property:DisplayName"),
        .. T(Guards, "Audio.AudioManager", "SetPlaylist", "PlayNextTrack", "ForcePlayTrack"),
        .. T(
            Guards,
            "Entities.TimeAndWeather.TimeOfDayComponent",
            "ctor:",
            "CycleTypeChangedHandler",
            "SetTimeOfDayChangedHandler"
        ),
        .. T(Guards, "Settings.LocalSettingsManager", "property:Settings"),
        .. T(Guards, "Settings.LocalSettings", "property:TimeOfDay"),
        .. T(Motion, "Settings.LocalSettings", "property:Interface"),
        .. T(Motion, "Settings.InterfaceSettings", "property:InterfaceAnimations"),
        .. More,
        .. T(Guards, "Settings.TimeOfDaySettings", "event:CycleTypeChanged", "event:SetTimeOfDayChanged"),
        .. T(
            Guards,
            "Saves.SaveManager",
            "WriteLocalSaveToActiveId",
            "CreateNewActiveSaveId",
            "SetActiveSaveId",
            "SetActiveSaveBackup",
            "ClearActiveSaveId",
            "WriteLocalActiveIdToCloud",
            "PerformPlatformCloudSync",
            "WriteSaveStringToAllSourcesAsync",
            "DeleteSaveWithIdAsync",
            "CopySaveWithIdAsync",
            "CopySaveStringAsync"
        ),
        .. T(
            Guards,
            "Notifications.NotificationManager",
            "AddNotification",
            "AddItemNotification",
            "AddCurrencyNotification",
            "AddMusicNotification",
            "AddAnnouncement",
            "AddSkillLevelUpAnnouncement",
            "AddQuestStepCompletedNotification",
            "AddQuestTaskGoalCompletedNotification",
            "AddMasteryGuildTaskCompletedNotification",
            "AddMasteryGuildTaskProgressedNotification",
            "AddNewItemFoundNotification"
        ),
        .. T(Guards, "Entities.Combat.CombatActionComponent", "PlayerDiedHandler", "event:PlayerDied", "TryStop"),
        .. T(Guards, "Entities.Combat.PlayerDiedEventArgs", "ctor:"),
        .. T(
            Engine,
            "Game",
            "ctor:",
            "LoadForEditor",
            "ReadFrom",
            "Dispose",
            "field:_characterName",
            "property:RNG",
            "property:Bank",
            "property:TimeAndWeather",
            "property:LeveledEntities"
        ),
        .. T(Engine, "Saves.SaveReader", "ctor:"),
        .. T(Engine, "Random.Xoshiro256StarStarRandom", "ctor:", "GetState", "RestoreState"),
        .. T(Engine, "Entities.TimeAndWeather.TimeAndWeatherEntity", "property:TimeOfDay"),
        .. T(
            Engine,
            "Entities.TimeAndWeather.TimeOfDayComponent",
            "property:SyncWithSystemClock",
            "property:IncrementTimer",
            "SetCurrentTime"
        ),
        .. T(Engine, "Core.TickTimer.TickTimer", "Pause"),
        .. T(Engine, "Entities.ILeveledEntity`1", "property:Experience"),
        .. T(Engine, "Components.Experience.ExperienceComponent", "property:LevelCap", "SetLevelCap"),
        .. T(
            Engine,
            "Entities.Combat.CombatEntity",
            "Tick",
            "property:Areas",
            "property:Action",
            "property:Player",
            "property:Enemy",
            "property:Toolbelt",
            "property:SlayerTasks",
            "property:RareEvents",
            "property:Loadouts"
        ),
        .. T(Engine, "Entities.Combat.CombatActionComponent", "property:IsPaused", "Unpause"),
        .. T(
            Engine,
            "Components.CombatAreas.CombatAreasComponent",
            "SelectEnemy",
            "SelectDungeon",
            "event:DungeonCompleted",
            "property:Config"
        ),
        .. T(Engine, "Components.CombatAreas.ICombatAreasConfig", "property:Areas"),
        .. T(Engine, "Components.CombatAreas.CombatArea", "property:Enemies"),
        .. T(Engine, "Components.CombatAreas.Dungeon"),
        .. T(Engine, "Components.CombatAreas.DungeonEventArgs"),
        .. T(Engine, "Components.Characters.EnemyComponent", "event:Killed", "property:Enemy"),
        .. T(
            Engine,
            "Components.Characters.EnemyKilledEventArgs",
            "property:SpawnTick",
            "property:KillTick",
            "property:Enemy"
        ),
        .. T(Engine, "Components.Characters.CharacterComponent", "event:Defended"),
        .. T(Engine, "Entities.Combat.AttackEventArgs", "property:HealthDamage"),
        .. T(
            Targets,
            "Components.CombatAreas.BaseCombatArea",
            "property:UnlockRequirements",
            "property:DisplayName",
            "property:Category"
        ),
        .. T(Targets, "Entities.Combat.Enemy", "property:Requirements", "property:DisplayName"),
        .. T(Targets, "Conditions.ConditionGroup", "property:Conditions"),
        .. T(Targets, "Conditions.Condition", "property:IsMet", "GetDescription"),
        .. T(Adjust, "Components.Characters.PlayerComponent", "property:Health", "property:Equipment"),
        .. T(Adjust, "Components.Characters.ICharacterHealth", "Set", "property:Max"),
        .. T(Adjust, "Components.SlayerTasks.SlayerTasksComponent", "property:ActiveMasters"),
        .. T(Adjust, "Components.SlayerTasks.ActiveSlayerMaster", "property:AutoSlayerEnabled"),
        .. T(
            Adjust,
            "Components.Experience.ExperienceComponent",
            "property:Xp",
            "property:Level",
            "SetXp",
            "event:XpChanged"
        ),
        .. T(Adjust, "Components.Inventories.IInventory", "AddQuantity", "RemoveQuantity", "event:ItemQuantityChanged"),
        .. T(Adjust, "Components.Inventories.AddItemFlags", "field:None"),
        .. T(
            Adjust,
            "Components.CurrencyWallets.ICurrencyWallet",
            "AddQuantity",
            "RemoveQuantity",
            "event:CurrencyQuantityChanged"
        ),
        .. T(Adjust, "Entities.Bank.BankEntity", "property:Inventory", "property:Wallet"),
        .. T(Adjust, "Components.Equipment.EquipmentComponent", "property:Config", "TryGetActiveSlot"),
        .. T(Adjust, "Components.Equipment.IEquipmentConfig", "property:EquipmentSlots"),
        .. T(Adjust, "Components.Equipment.IActiveEquipmentSlot", "event:QuantityChanged"),
        .. T(
            Adjust,
            "Components.Equipment.ActiveEquipmentSlot",
            "AddQuantity",
            "property:IsEmpty",
            "property:IsOccupied",
            "property:Quantity",
            "property:Equipped"
        ),
        .. T(Adjust, "Components.Equipment.EquipmentItem", "property:Item", "property:Equipment"),
        .. T(Adjust, "Components.Equipment.EquipmentCapability", "property:CanEquipQuantity"),
        .. T(Adjust, "Components.Toolbelt.ToolbeltComponent", "field:_activeSlots"),
        .. T(
            Adjust,
            "Components.Toolbelt.ActiveToolbeltSlot",
            "event:QuantityChanged",
            "AddQuantity",
            "property:Quantity",
            "property:Equipped"
        ),
        .. T(Adjust, "Components.Toolbelt.ToolbeltItem", "property:Item"),
        .. T(Tally, "Components.Inventories.ItemQuantityChangedEventArgs", "property:Item"),
        .. T(Tally, "Components.CurrencyWallets.CurrencyQuantityChangedEventArgs", "property:Currency"),
        .. T(Tally, "Core.ValueChangedEventArgs`1", "property:OldValue", "property:NewValue"),
        .. T(RareEvents, "Components.RareEvents.RareEventsComponent", "event:EventObserved"),
        .. T(RareEvents, "Components.RareEvents.RareEventObservedEventArgs", "property:Event"),
        .. T(RareEvents, "Components.RareEvents.RareEvent", "property:DisplayName"),
        // Hits and HP from the player's own health, before anything heals, and a dungeon's stage count
        .. T(Risk, "Components.Characters.PlayerComponent", "property:GlobalHealth"),
        .. T(Risk, "Components.Characters.CharacterHealth", "event:Damaged", "property:Current", "property:Max"),
        .. T(Risk, "Components.Characters.HealthDamagedEventArgs", "property:Damage", "property:Amount"),
        .. T(Risk, "Components.Characters.ModifiedDamage", "property:Modified"),
        .. T(Risk, "Components.CombatAreas.Dungeon", "property:EnemyCount"),
        .. T(Values, "Data.Items.Item", "property:SellsFor", "property:DisplayName"),
        .. T(Values, "Data.Currencies.CurrencyQuantity", "property:Currency", "property:Quantity"),
        .. T(Values, "Data.Currencies.Currency", "property:DisplayName"),
        .. T(Values, "Data.IDataRegistryOutput`1", "GetAllObjects"),
        .. T(Fingerprint, "Game", "property:PassiveActionManager", "property:EventLog"),
        .. T(Fingerprint, "Saves.SaveWriter", "Write"),
        // The stance's main skill, for ranking by "XP · main skill" (Copy)
        .. T(MainSkill, "Components.Characters.CharacterComponent", "property:Stats"),
        .. T(MainSkill, "Components.Characters.CharacterStats", "property:AttackStance"),
        .. T(MainSkill, "Entities.Combat.AttackStance", "property:ExperienceGain", "property:Icon"),
        .. T(MainSkill, "Entities.Combat.DamageExperienceGain", "property:Component", "property:ExperiencePerDamage"),
        .. T(MainSkill, "Components.GameComponent", "property:Entity"),
        // The setup bar's gear summary (Copy)
        .. T(Gear, "Components.Equipment.EquipmentComponent", "field:_activeSlots"),
        .. T(Gear, "Components.Equipment.ActiveEquipmentSlot", "property:Equipped", "property:GridPosition"),
        .. T(Gear, "Components.CombatFood.FoodComponent", "property:SelectedFood"),
        .. T(Gear, "Components.CombatFood.FoodItem", "property:Item"),
        .. T(Gear, "Entities.Combat.CombatEntity", "property:Food"),
        .. T(Gear, "Entities.Combat.AttackStance", "property:CombatStyle", "property:DisplayName"),
        .. T(Gear, "Entities.Combat.CombatStyle", "property:DisplayName"),
        .. T(Gear, "Components.Equipment.EquipmentComponent", "property:Loadouts"),
        .. T(Gear, "Components.Equipment.EquipmentLoadout", "property:Slots"),
        .. T(Gear, "Components.Equipment.EquipmentSlotLoadout", "property:Equipped"),
        .. T(Gear, "Components.CombatFood.FoodComponent", "property:Loadouts"),
        .. T(Gear, "Components.Characters.PlayerComponent", "property:LoadoutStances"),
        .. T(Gear, "Components.Loadouts.LoadoutComponent", "property:CurrentLoadout"),
        // Changes tried in copies (Changes, ChangeCheck, RequirementsIgnored)
        .. T(Changes, "Components.Experience.ExperienceComponent", "SetXp", "property:LevelCap", "property:Table"),
        .. T(Changes, "Components.ItemEquipping.ItemEquippingComponent", "EquipItem", "UnequipSlot"),
        .. T(Changes, "Entities.Combat.CombatEntity", "property:ItemEquipping", "property:Food", "property:Prayer"),
        .. T(
            Changes,
            "Components.Equipment.EquipmentCapability",
            "property:Requirements",
            "property:Type",
            "property:CanEquipQuantity"
        ),
        .. T(
            Changes,
            "Components.Equipment.EquipmentSlot",
            "property:UnlockRequirements",
            "property:FitsType",
            "property:DisplayName"
        ),
        .. T(
            Changes,
            "Components.Equipment.EquipmentComponent",
            "TryGetActiveSlot",
            "IsItemEquipped",
            "field:_activeSlots"
        ),
        .. T(Changes, "Components.Equipment.IActiveEquipmentSlot", "property:Slot", "property:Equipped"),
        .. T(Changes, "Entities.Bank.BankInventoryComponent", "AddQuantity", "GetQuantity"),
        .. T(Changes, "Components.Inventories.AddItemFlags", "field:None"),
        .. T(Changes, "Conditions.ConditionGroup", "property:AreMet", "property:Conditions"),
        .. T(Changes, "Conditions.Condition", "property:IsMet", "GetDescription"),
        .. T(Changes, "Data.Items.Item", "property:Capabilities", "property:IgnoreCompletion"),
        .. T(Changes, "Components.CombatFood.FoodComponent", "SelectFood", "UnselectFood", "property:SelectedFood"),
        .. T(Changes, "Components.CombatFood.FoodCapability"),
        .. T(
            Changes,
            "Components.Prayers.PrayerComponent",
            "TogglePrayer",
            "IsPrayerSelected",
            "IsPrayerActive",
            "property:ActivePrayers",
            "property:ActiveSlots",
            "property:Wallet"
        ),
        .. T(
            Changes,
            "Components.Prayers.Prayer",
            "property:UnlockRequirements",
            "property:FitsInSlot",
            "property:Cost",
            "property:DisplayName"
        ),
        .. T(
            Changes,
            "Components.Prayers.ActivePrayerSlot",
            "property:FitsType",
            "property:UnlockRequirements",
            "property:Selected"
        ),
        .. T(Changes, "Components.CurrencyWallets.ICurrencyWallet", "GetQuantity"),
        .. T(Changes, "Components.Toolbelt.ToolbeltCapability", "property:FitsInSlot"),
        .. T(Changes, "Components.Toolbelt.ToolbeltSlot", "property:FitsType"),
        .. T(Changes, "Components.Characters.PlayerComponent", "SetSelectedStance", "property:Equipment"),
        .. T(Changes, "Components.Equipment.EquipmentComponent", "property:CurrentWeapon"),
        .. T(Changes, "Components.Equipment.WeaponType", "property:Stances"),
        .. T(Changes, "Components.Prayers.ActivePrayerSlot", "property:Data"),
        .. T(Changes, "Entities.Combat.CombatEntity", "property:MagicSpells"),
        .. T(
            Changes,
            "Components.MagicSpells.MagicSpellsComponent",
            "SelectSpell",
            "SelectImbuement",
            "DeselectImbuement",
            "property:SelectedSpell",
            "property:SelectedImbuement",
            "property:ActiveSpells",
            "property:ActiveImbuements",
            "property:AvailableElements",
            "property:CanUseSpells"
        ),
        .. T(
            Changes,
            "Components.MagicSpells.BaseMagicSpell",
            "property:Element",
            "property:RuneCosts",
            "property:UnlockRequirements"
        ),
        .. T(Changes, "Data.Items.ItemQuantity", "property:Item", "property:Quantity"),
        .. T(Changes, "Components.Toolbelt.ActiveToolbeltSlot", "property:Slot", "property:Equipped"),
        .. DropTables,
        // A setup's stats and modifiers, read from a check copy (SetupSheets)
        .. T(
            SetupSheet,
            "Components.Characters.CharacterStats",
            "property:ModifiedCombatLevels",
            "property:CombatStyle",
            "property:AttackStance",
            "property:Element",
            "property:AccuracyRating",
            "property:AttackInterval",
            "property:MaxHitpoints",
            "property:EvasionRatings",
            "property:PowerRatings",
            "property:ProtectionRatings",
            "property:ModifiedCombatStatTotals",
            "property:CriticalHitChance",
            "property:CriticalHitMultiplier",
            "property:PercentAttackReflect",
            "property:FlatLight"
        ),
        .. T(SetupSheet, "Stats.Nodes.DictionaryStatNode`2", "property:Dict"),
        .. T(SetupSheet, "Stats.Nodes.ValueStatNode`1", "property:Value"),
        .. T(SetupSheet, "Stats.Nodes.ModifierStatNodes", "property:Total"),
        .. T(SetupSheet, "Entities.Combat.AllAttacksDamageWatcher", "ctor:"),
        .. T(SetupSheet, "Entities.Combat.DamageWatcher", "property:Minimum", "property:Maximum", "Dispose"),
        .. T(SetupSheet, "Entities.Combat.CombatStyle", "ComputeCombatLevel", "property:DisplayName"),
        .. T(SetupSheet, "Components.Characters.CharacterComponent", "property:CombatLevels"),
        .. T(SetupSheet, "Components.CombatLevels.CombatLevel", "property:DisplayName", "property:DisplayPriority"),
        .. T(SetupSheet, "Entities.Combat.Element", "property:DisplayName"),
        .. T(SetupSheet, "Entities.Combat.DamageType", "property:ResistanceNameShort"),
        .. T(SetupSheet, "Entities.Combat.AttackStance", "property:DisplayName"),
        .. T(
            SetupSheet,
            "Entities.Combat.CombatEntity",
            "property:DamageTypeDisplayOrder",
            "property:Config",
            "property:Equipment",
            "property:Toolbelt",
            "property:Food"
        ),
        .. T(
            SetupSheet,
            "Entities.Combat.CombatEntityConfig",
            "property:AttackStances",
            "property:OffensiveStatCategories",
            "property:DefensiveStatCategories"
        ),
        .. T(
            SetupSheet,
            "Components.Equipment.EquipmentComponent",
            "property:CombatStatsRegistry",
            "field:_activeSlots"
        ),
        .. T(
            SetupSheet,
            "Components.CombatStats.CombatStat",
            "property:DisplayName",
            "property:NumberFormat",
            "property:Category",
            "property:DisplayPriority"
        ),
        .. T(SetupSheet, "Localization.LocalizedString", "Format", "property:Text"),
        .. T(SetupSheet, "Game", "property:Modifiers"),
        .. T(SetupSheet, "Modifiers.ModifierSystem", "property:Values"),
        .. T(SetupSheet, "Modifiers.ModifierValues", "property:Global", "property:Component", "property:CombatEffect"),
        .. T(
            SetupSheet,
            "Modifiers.IModifierValue",
            "GetDescription",
            "property:Kind",
            "property:IsEnemy",
            "property:Condition",
            "property:Scaling"
        ),
        .. T(
            SetupSheet,
            "Components.CombatEffects.CombatEffectModifierValue",
            "property:Modifier",
            "property:Chance",
            "property:IsEnemy",
            "property:Condition",
            "property:Scaling"
        ),
        .. T(
            SetupSheet,
            "Components.CombatEffects.CombatEffectModifier",
            "property:Effect",
            "property:AppliesTo",
            "property:Kind"
        ),
        .. T(SetupSheet, "Components.CombatEffects.ConfiguredCombatEffect", "GetDescription"),
        .. T(SetupSheet, "ScalingFactors.ScalingFactor", "property:PerDescription"),
        .. T(SetupSheet, "Conditions.Condition", "GetDescription"),
        .. T(Art, "Entities.Combat.Enemy", "property:Sprite"),
        .. T(
            Art,
            "Components.CombatAreas.BaseCombatArea",
            "property:CoverBackground",
            "property:Icon",
            "property:Category"
        ),
        .. T(Art, "Components.CombatAreas.CombatAreaCategory", "property:Icon"),
        .. T(Art, "Data.Items.Item", "property:Icon"),
        .. T(Art, "Data.Currencies.Currency", "property:Icon"),
        .. T(Art, "Entities.GameEntity", "property:Icon"),
        .. Game.Art.Paths.Select(path => new Dependency("file", path, Art)),
        .. Game.Art.Boxes.Select(box => new Dependency(
            "file",
            Game.Art.BoxFolder + box + ".tres",
            Game.Art.BoxFeature
        )),
        D(Engine, "entity", LiveGame.CombatId),
        .. Later,
    ];
}
