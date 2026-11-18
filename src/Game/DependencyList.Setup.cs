namespace MelvorMods.CombatSimulator2.Game;

// The catalogue's rows for what a setup page changes: one feature per game system, so a system a game update
// moves turns off its own page and nothing else.
internal static partial class Dependencies
{
    public const string CharacterKey = "character-key";
    public const string Toolbelt = "change-toolbelt";
    public const string Astrology = "page-astrology";
    public const string Guild = "page-guild";
    public const string Shop = "page-shop";
    public const string Quests = "page-quests";
    public const string Herblore = "page-herblore";
    public const string Pets = "page-pets";
    public const string Firemaking = "page-firemaking";
    public const string Agility = "page-agility";

    // A property, not a field: the table is built while this type's fields are still being set, in no promised
    // order across its files.
    private static IEnumerable<Dependency> SetupPages =>
        [
            .. T(CharacterKey, "Saves.SaveManager", "property:HasActiveSaveId", "property:ActiveSaveId"),
            .. T(Changes, "Entities.Bank.BankInventoryComponent", "RemoveQuantity", "property:Stacks"),
            .. T(Changes, "Entities.Bank.BankEntity", "property:Wallet"),
            .. T(Changes, "Components.CurrencyWallets.ICurrencyWallet", "AddQuantity"),
            .. T(Changes, "Data.Recipes.RecipeIngredients", "property:Items", "property:Currencies"),
            .. T(Toolbelt, "Entities.Combat.CombatEntity", "property:ToolbeltEquipping", "property:Toolbelt"),
            .. T(Toolbelt, "Components.ToolbeltEquipping.ToolbeltEquippingComponent", "EquipItem", "UnequipSlot"),
            .. T(Toolbelt, "Components.Toolbelt.ToolbeltComponent", "TryGetActiveSlot", "IsItemEquipped"),
            .. T(Toolbelt, "Components.Toolbelt.IActiveToolbeltSlot", "property:Equipped"),
            .. T(
                Toolbelt,
                "Components.Toolbelt.ToolbeltCapability",
                "property:Requirements",
                "property:CanEquipQuantity"
            ),
            .. T(Toolbelt, "Components.Toolbelt.ToolbeltSlot", "property:UnlockRequirements"),
            .. T(Astrology, "Entities.Astrology.AstrologyEntity", "property:Action"),
            .. T(
                Astrology,
                "Entities.Astrology.AstrologyActionComponent",
                "TryGetMinorNodeConstellation",
                "TryGetMajorNodeConstellation",
                "property:ActiveConstellations"
            ),
            .. T(
                Astrology,
                "Entities.Astrology.ActiveAstrologyConstellation",
                "IsMinorNodeUnlocked",
                "IsMajorNodeUnlocked",
                "UnlockMinorNode",
                "UnlockMajorNode",
                "AddStudyKnowledge",
                "property:Knowledge",
                "property:MinorNodeUnlockCount",
                "property:AvailableMinorNodeCount",
                "property:MajorNodeUnlockCount",
                "property:AvailableMajorNodeCount"
            ),
            .. T(
                Astrology,
                "Entities.Astrology.AstrologyConstellation",
                "property:MinorNodeUnlockKnowledge",
                "property:MinorNodesPerMajorNode"
            ),
            .. T(Astrology, "Entities.Astrology.AstrologyNode", "property:UnlockRequirements"),
            .. T(Astrology, "GameMenus.Pages.SkillNavigationOption", "property:Entity"),
            .. T(Astrology, "GameMenus.Pages.PageNavigationOption", "property:Requirements"),
            D(Astrology, "entity", StarChange.AstrologyId),
            .. T(
                Guild,
                "Components.MasteryGuild.MasteryGuildComponent",
                "field:_orderedRanks",
                "property:CurrentRank",
                "property:CurrentPoints",
                "property:ActiveTasks",
                "GetPointsForRank",
                "GetRankForPoints"
            ),
            .. T(
                Guild,
                "Components.MasteryGuild.ActiveMasteryGuildTask",
                "property:ActiveGoal",
                "property:ConditionGoal",
                "property:TriggerGoal",
                "property:IsComplete",
                "property:Category"
            ),
            .. T(Guild, "Components.MasteryGuild.MasteryGuildCategory", "property:BasePoints"),
            .. T(Guild, "Goals.IActiveGoal", "SetComplete", "Reset", "property:IsComplete"),
            .. T(Shop, "Entities.Shop.ShopEntity", "property:Shop"),
            .. T(Shop, "Components.Shop.ShopComponent", "GetUpgradesPurchased", "ProcessPurchase"),
            .. T(
                Shop,
                "Entities.Shop.ShopPurchase",
                "property:Requirements",
                "property:Provides",
                "property:Costs",
                "property:DefaultBuyLimit",
                "property:DisplayName"
            ),
            .. T(
                Shop,
                "Entities.Shop.ShopPurchaseProvides",
                "property:Bonuses",
                "property:Items",
                "property:Currencies"
            ),
            .. T(Shop, "Core.InventoryWalletTransactions.InventoryWalletTransactionResult", "field:Success"),
            .. T(Quests, "Entities.Quests.QuestsEntity", "property:Quests"),
            .. T(Quests, "Entities.Quests.Quest", "property:DisplayName"),
            .. T(
                Quests,
                "Entities.Quests.QuestsComponent",
                "property:ActiveQuests",
                "ExecuteTaskCosts",
                "ClaimRewards"
            ),
            .. T(
                Quests,
                "Entities.Quests.ActiveQuest",
                "Reset",
                "Start",
                "property:State",
                "property:Requirements",
                "property:ActiveSteps"
            ),
            .. T(Quests, "Entities.Quests.ActiveQuestStep", "property:State", "property:ActiveTasks"),
            .. T(
                Quests,
                "Entities.Quests.ActiveQuestTask",
                "property:State",
                "property:ActiveGoals",
                "property:CostsMet",
                "property:Costs"
            ),
            .. T(Quests, "Entities.Quests.QuestCompleteCondition", "property:Quest"),
            .. T(Quests, "Entities.Quests.QuestState", "field:Claimed", "field:Completed", "field:InProgress"),
            .. T(Quests, "Entities.Quests.QuestStepState", "field:Active"),
            .. T(
                Herblore,
                "Entities.Herblore.HerbloreRecipe",
                "property:PotionGroup",
                "property:Potency",
                "property:PotionClass",
                "property:Products"
            ),
            .. T(Herblore, "Components.Toolbelt.ToolbeltCapability", "property:FitsInSlot", "property:Bonuses"),
            .. T(Herblore, "Components.Toolbelt.ToolbeltSlot", "property:FitsType"),
            .. T(Herblore, "Components.Toolbelt.ToolbeltComponent", "field:_activeSlots", "TryGetActiveSlot"),
            .. T(Herblore, "Components.ToolbeltEquipping.ToolbeltEquippingComponent", "EquipItem", "UnequipSlot"),
            .. T(Pets, "Components.Pets.PetsComponent", "property:ActivePets"),
            .. T(Pets, "Components.Pets.ActivePet", "property:IsUnlocked", "property:Pet"),
            .. T(Pets, "Components.Pets.Pet", "property:Bonuses", "property:DisplayName", "property:Icon"),
            .. T(Pets, "Game", "property:Modifiers"),
            .. T(Pets, "Modifiers.ModifierSystem", "AddOrUpdate", "Remove"),
            .. T(Firemaking, "Entities.Firemaking.FiremakingEntity", "property:Bonfire"),
            .. T(
                Firemaking,
                "Entities.Firemaking.FiremakingBonfireComponent",
                "property:CurrentBonfire",
                "property:NextBonfire",
                "property:Bonfires",
                "property:IsActive",
                "property:DurationTimer",
                "PurchaseNextBonfire"
            ),
            .. T(
                Firemaking,
                "Core.FillableTimers.FillableTimer",
                "property:TicksLeft",
                "property:MaxTicks",
                "property:HasTime"
            ),
            .. T(
                Firemaking,
                "Entities.Firemaking.FiremakingBonfire",
                "property:Bonuses",
                "property:Requirements",
                "property:Costs",
                "property:MaxDuration"
            ),
            .. T(Agility, "Entities.Agility.AgilityEntity", "property:Techniques", "property:Action"),
            .. T(Agility, "Entities.Agility.AgilityTechniquesComponent", "property:ActiveTechniques"),
            .. T(Agility, "Entities.Agility.ActiveAgilityTechnique", "property:Experience", "property:Technique"),
            .. T(
                Agility,
                "Entities.Agility.AgilityTechnique",
                "property:UnlockRequirements",
                "property:DisplayName",
                "property:Icon"
            ),
            .. T(Agility, "Components.Experience.BaseExperience", "SetXp", "property:Table", "property:LevelCap"),
            .. T(Agility, "Entities.Agility.AgilityTechniqueLevelCondition", "property:Technique", "property:Level"),
            .. T(Agility, "Entities.Agility.ActiveAgilityCourse", "GetShortcutsCreatedCount", "property:Course"),
            .. T(Agility, "Entities.Agility.AgilityActionComponent", "property:Courses"),
            .. T(Agility, "Entities.Agility.AgilityCourse", "property:Location", "property:DisplayName"),
            .. T(Agility, "Entities.Agility.AgilityTechnique", "property:DifficultyMetBonuses"),
            .. T(Agility, "Entities.Agility.AgilityObstacleModifierTemplate", "CreateModifiers"),
            .. T(Agility, "Entities.Agility.Statistics.AgilityCourseStatistic"),
        ];
}
