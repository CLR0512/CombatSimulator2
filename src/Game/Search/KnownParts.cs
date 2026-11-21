using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Game.Search;

/// <summary>
/// Names a closed set of the game's kinds: every concrete class under <see cref="Base"/> (a type name relative to
/// the game's namespace) is expected to be one of <see cref="Known"/>. A class the game has and the list lacks is a
/// kind of part the search has no rule for. <c>./mm check</c> reads this attribute by name against each game build,
/// and <c>cs2 deps</c> reads it in the running game.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class ClosedSetAttribute(string what, string @base, params string[] known) : Attribute
{
    /// <summary>What a member of the set is, in the words a report uses: "effect kind".</summary>
    public string What { get; } = what;
    public string Base { get; } = @base;
    public IReadOnlyList<string> Known { get; } = known;
}

/// <summary>
/// The kinds of part the search has a rule for. Its rules are few, because it models no fight: stats and modifiers
/// are compared in the game's own direction when the stat is listed here with what it acts on; an item with an
/// effect, an attack of its own or a use-up trigger is a kind of its own and is only ever fought. So a kind that is
/// missing here never gives a wrong answer: it makes the items carrying it opaque, which costs fights.
/// </summary>
[ClosedSet(
    "effect kind",
    "Components.CombatEffects.CombatEffect",
    "AttackChargingEffect",
    "AttackCountingEffect",
    "DamageOverTimeEffect",
    "EndOfTurnDamageEffect",
    "EndOfTurnHealingEffect",
    "RandomMagnitudeEffect",
    "StackingEffect",
    "StunEffect",
    "TurnCountingEffect"
)]
[ClosedSet(
    "effect trigger",
    "Components.CombatEffects.CombatEffectTrigger",
    "AtStartOfFightCombatEffectTrigger",
    "FoodEatenCombatEffectTrigger",
    "OnAttackCombatEffectTrigger",
    "OnDeathCombatEffectTrigger",
    "WhenAttackedCombatEffectTrigger"
)]
[ClosedSet(
    "damage formula",
    "Entities.Combat.DamageFormula",
    "CurrentHitpointsDamageFormula",
    "DamageDealtDamageFormula",
    "FixedDamageFormula",
    "FixedRangeDamageFormula",
    "MaxHitDamageFormula",
    "MaxHitpointsDamageFormula",
    "NormalDamageFormula"
)]
[ClosedSet(
    "max hit formula",
    "Entities.Combat.MaxHitFormula",
    "DamageBonusMaxHitFormula",
    "StrengthBonusMaxHitFormula"
)]
[ClosedSet(
    "item capability",
    "Data.Items.ItemCapability",
    "BuryCapability",
    "CompostCapability",
    "EquipmentCapability",
    "FishingBaitCapability",
    "FoodCapability",
    "HuntingTrapCapability",
    "OpenableCapability",
    "ToolbeltCapability",
    "WeaponCapability"
)]
[ClosedSet(
    "trigger kind",
    "Triggers.Trigger",
    "AgilityCourseCompletedTrigger",
    "AgilityDamageAvoidedTrigger",
    "AgilityDamagedPlayerTrigger",
    "AgilityObstacleAttemptedTrigger",
    "AgilityObstacleFailedTrigger",
    "AmmoConsumedTrigger",
    "AnimalDiedTrigger",
    "AnimalFedTrigger",
    "AnimalGrewUpTrigger",
    "AnimalPurchasedTrigger",
    "AttackedTrigger",
    "AutomationCostsConsumedTrigger",
    "BurnRecipeTrigger",
    "CatchSpecialItemTrigger",
    "ChancePerExperienceTrigger",
    "ConstellationStudiedTrigger",
    "CookRecipeTrigger",
    "CreateRecipeTrigger",
    "CurrencyGainedTrigger",
    "CurrencyLostTrigger",
    "CutTreeTrigger",
    "DamageTreeTrigger",
    "DaySegmentChangedTrigger",
    "DefendedTrigger",
    "DiedTrigger",
    "DungeonCompletedTrigger",
    "EarnTaskCurrencyTrigger",
    "EnemyKilledTrigger",
    "EnemySpawnedTrigger",
    "EventObservedTrigger",
    "FightEndedTrigger",
    "FightStartedTrigger",
    "FishRecipeTrigger",
    "FoodEatenTrigger",
    "GainCurrencyTrigger",
    "GainExperienceTrigger",
    "GainItemTrigger",
    "GainLevelTrigger",
    "GlobalActionPerformedTrigger",
    "GlobalFoodEatenTrigger",
    "GlobalPetUnlockedTrigger",
    "GlobalToolbeltItemConsumedTrigger",
    "ItemConsumedTrigger",
    "ItemEquippedTrigger",
    "ItemUpgradedTrigger",
    "ItemsGainedTrigger",
    "ItemsLostTrigger",
    "ItemsOpenedTrigger",
    "MineRockTrigger",
    "PerformStepTrigger",
    "PetAnimalTrigger",
    "PlotCompostedTrigger",
    "PurchaseBoughtTrigger",
    "QuestCompletedTrigger",
    "QuestStepCompletedTrigger",
    "RankChangedTrigger",
    "RareBonusesEventActivatedTrigger",
    "RareEventObservedTrigger",
    "RareEventTimerFiredTrigger",
    "RecipeGrownTrigger",
    "RecipeHarvestedTrigger",
    "RecipePlantedTrigger",
    "RecipeWateredTrigger",
    "RunesUsedTrigger",
    "SameAnimalCountTrigger",
    "SkipStepTrigger",
    "SpendCurrencyTrigger",
    "SpendItemTrigger",
    "TaskCompletedTrigger",
    "TaskProgressedTrigger",
    "ThievingActionPerformedTrigger",
    "ThievingStunAvoidedTrigger",
    "ThievingStunnedTrigger",
    "ToolbeltItemConsumedTrigger",
    "ToolbeltItemEquippedTrigger",
    "TurnEndedTrigger",
    "WeatherChangedTrigger",
    "AttackTurnBufferedTrigger"
)]
[ClosedSet(
    "condition kind",
    "Conditions.Condition",
    "AgilityNumTechniquesLevelCondition",
    "AgilityShortcutsCreatedCondition",
    "AgilityTechniqueLevelCondition",
    "ConstellationKnowledgeCondition",
    "CurrencyOwnedCondition",
    "DemoLockedCondition",
    "EnemyCombatLevelCondition",
    "ExperienceCondition",
    "FightingSlayerTaskCondition",
    "HasArmourCondition",
    "HasWardCondition",
    "HuntingAnimalLevelCondition",
    "HuntingMaxLevelAnimalsCondition",
    "HuntingUniqueCollectablesFoundCondition",
    "HuntingUniqueRareSpeciesFoundCondition",
    "InAreaCondition",
    "IsActiveCondition",
    "ItemCompletionCondition",
    "ItemEquippedCondition",
    "ItemOwnedCondition",
    "LevelCondition",
    "LightCondition",
    "MajorNodeUnlockCountCondition",
    "MajorNodeUnlockedCondition",
    "MasteryGuildCompletionCondition",
    "MasteryGuildPointsCondition",
    "MasteryGuildRankCondition",
    "MaxCurrencyOwnedCondition",
    "MaxItemOwnedCondition",
    "MinorNodeUnlockCountCondition",
    "MinorNodeUnlockedCondition",
    "NoWeaponEquippedCondition",
    "OnboardingCompleteCondition",
    "PetUnlockedCondition",
    "PetsCompletionCondition",
    "PrayerActiveCondition",
    "QuestActiveStepCondition",
    "QuestActiveTaskGoalCondition",
    "QuestCompleteCondition",
    "QuestStepCompleteCondition",
    "ShopPurchaseCondition",
    "SkillCompletionCondition",
    "SlotEquippedCondition",
    "ToolbeltItemEquippedCondition",
    "ToolbeltItemNotEquippedCondition",
    "TotalCompletionCondition",
    "TotalPrayerCostActiveCondition",
    "UsingAttackStanceCondition",
    "UsingCombatStyleCondition",
    "UsingElementCondition",
    "UsingImbuementCondition",
    "UsingSpellBookCondition",
    "UsingSpellCondition",
    "AgilityCourseStatisticCondition",
    "AstrologyEventStatisticCondition",
    "CombatStyleStatisticCondition",
    "ConstellationStatisticCondition",
    "CurrencyStatisticCondition",
    "DungeonStatisticCondition",
    "EnemyStatisticCondition",
    "FarmingCategoryStatisticCondition",
    "FarmingRecipeHarvestedStatisticCondition",
    "HuntingAnimalSpeciesStatisticCondition",
    "HuntingAnimalStatisticCondition",
    "ItemStatisticCondition",
    "PrayerStatisticCondition",
    "RanchingAnimalStatisticCondition",
    "SlayerMasterStatisticCondition",
    "ValueStatisticCondition"
)]
internal static partial class KnownParts
{
    /// <summary>Whether <paramref name="item"/> is to be read as carrying a part the search doesn't know.</summary>
    public static bool IsUnknown(string item)
    {
        var unknown = false;
        Unknown(item, ref unknown);
        return unknown;
    }

    // Dev builds only: one item made unknown for the session, to see that it is fought and never compared.
    static partial void Unknown(string item, ref bool unknown);

    /// <summary>
    /// The modifiable stats the search knows, as <c>stats class.name</c>, each with what it acts on, so that a goal
    /// that doesn't count it can leave it out. Any other stat the game labels good or bad is compared as acting on
    /// everything, unless its label misleads (<see cref="Model.Search.ModifierRule.Misleading"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, PartGroup> Stats { get; } =
        new Dictionary<string, PartGroup>(StringComparer.Ordinal)
        {
            ["MelvorBase:Character.FlatAttackInterval"] = PartGroup.Fight,
            ["MelvorBase:Character.PercentAttackInterval"] = PartGroup.Fight,
            ["MelvorBase:Character.FlatCriticalHitChance"] = PartGroup.Fight,
            ["MelvorBase:Character.FlatCriticalHitMultiplier"] = PartGroup.Fight,
            ["MelvorBase:Character.DoubleWardDamageChance"] = PartGroup.Fight,
            ["MelvorBase:Character.FlatMaxHit"] = PartGroup.Fight,
            ["MelvorBase:Character.FlatMinHit"] = PartGroup.Fight,
            ["MelvorBase:Character.FlatPowerRating"] = PartGroup.Fight,
            ["MelvorBase:Character.MaxHitCombatTriangle"] = PartGroup.Fight,
            ["MelvorBase:Character.MinHitBasedOnMaxHit"] = PartGroup.Fight,
            ["MelvorBase:Character.PercentAccuracyRating"] = PartGroup.Fight,
            ["MelvorBase:Character.PercentDamageDealt"] = PartGroup.Fight,
            ["MelvorBase:Character.PercentMaxHit"] = PartGroup.Fight,
            ["MelvorBase:Character.PercentPowerRating"] = PartGroup.Fight,
            ["MelvorBase:Character.AttackDamageNegationChance"] = PartGroup.Defence,
            ["MelvorBase:Character.FlatEvasionRating"] = PartGroup.Defence,
            ["MelvorBase:Character.FlatMaxHitpoints"] = PartGroup.Defence,
            ["MelvorBase:Character.FlatProtectionRating"] = PartGroup.Defence,
            ["MelvorBase:Character.PercentAttackReflect"] = PartGroup.Defence,
            ["MelvorBase:Character.PercentDamageTaken"] = PartGroup.Defence,
            ["MelvorBase:Character.PercentEvasionRating"] = PartGroup.Defence,
            ["MelvorBase:Character.PercentMaxHitpoints"] = PartGroup.Defence,
            ["MelvorBase:Character.PercentProtectionRating"] = PartGroup.Defence,
            ["MelvorBase:Character.FlatLight"] = PartGroup.Other,
            ["MelvorBase:Character.FlatDarkness"] = PartGroup.Other,
            ["MelvorBase:Character.PercentDarkness"] = PartGroup.Other,
            ["MelvorBase:Target.FlatHitChance"] = PartGroup.Fight,
            ["MelvorBase:Target.FlatResistance"] = PartGroup.Defence,
            ["MelvorBase:Target.PercentResistance"] = PartGroup.Defence,
            ["MelvorBase:Target.ResistanceCombatTriangle"] = PartGroup.Defence,
            ["MelvorBase:Target.PercentDamageTakenFrom"] = PartGroup.Defence,
            ["MelvorBase:Target.PercentDamageTakenFromAttacks"] = PartGroup.Defence,
            ["MelvorBase:CombatEffect.EffectLifesteal"] = PartGroup.Defence,
            ["MelvorBase:CombatEffect.PercentDamageTaken"] = PartGroup.Defence,
            ["MelvorBase:CombatFood.FlatAutoEatEfficiency"] = PartGroup.Defence,
            ["MelvorBase:CombatFood.FlatAutoEatLimit"] = PartGroup.Defence,
            ["MelvorBase:CombatFood.FlatAutoEatThreshold"] = PartGroup.Defence,
            ["MelvorBase:CombatFood.FlatHealing"] = PartGroup.Defence,
            ["MelvorBase:CombatFood.PercentHealing"] = PartGroup.Defence,
            ["MelvorBase:CombatFood.FoodPreservationChance"] = PartGroup.Costs,
            ["MelvorBase:AmmoConsumption.AmmoPreservationChance"] = PartGroup.Costs,
            ["MelvorBase:Equipment.PreservationChance"] = PartGroup.Costs,
            ["MelvorBase:MagicSpell.FlatRuneCost"] = PartGroup.Costs,
            ["MelvorBase:MagicSpell.RunePreservationChance"] = PartGroup.Costs,
            ["MelvorBase:Prayer.CostPreservationChance"] = PartGroup.Costs,
            ["MelvorBase:Prayer.FlatPrayerCost"] = PartGroup.Costs,
            ["MelvorBase:Prayer.PercentPrayerCost"] = PartGroup.Costs,
            ["MelvorBase:CombatLoot.BuriableDoublingChance"] = PartGroup.Rewards,
            ["MelvorBase:CombatLoot.CurrencyDoublingChance"] = PartGroup.Rewards,
            ["MelvorBase:CombatLoot.ItemDoublingChance"] = PartGroup.Rewards,
            ["MelvorBase:Experience.FlatXPGained"] = PartGroup.Xp,
            ["MelvorBase:Experience.PercentXPGained"] = PartGroup.Xp,
        };

    /// <summary>The game-wide modifier groups a fight reads, with what they act on.</summary>
    public static IReadOnlyDictionary<string, PartGroup> GlobalGroups { get; } =
        new Dictionary<string, PartGroup>(StringComparer.Ordinal)
        {
            ["GlobalFlatXpGained"] = PartGroup.Xp,
            ["GlobalPercentXpGained"] = PartGroup.Xp,
            ["GlobalFlatAutoEatThreshold"] = PartGroup.Defence,
            ["GlobalFlatAutoEatEfficiency"] = PartGroup.Defence,
            ["GlobalFlatAutoEatLimit"] = PartGroup.Defence,
        };

    /// <summary>The one equipment stat that is better lower: the game itself names it to paint it neutral.</summary>
    public const string AttackInterval = "MelvorBase:AttackInterval";

    /// <summary>
    /// The equipment stat categories with what they act on. A stat of a category missing here, and one of the
    /// category for the rest that isn't in <see cref="OtherStats"/>, makes its item opaque.
    /// </summary>
    public static IReadOnlyDictionary<string, PartGroup> StatCategories { get; } =
        new Dictionary<string, PartGroup>(StringComparer.Ordinal)
        {
            ["MelvorBase:MeleeAttackBonus"] = PartGroup.Fight,
            ["MelvorBase:RangedAttackBonus"] = PartGroup.Fight,
            ["MelvorBase:MagicAttackBonus"] = PartGroup.Fight,
            // The strength and damage bonuses of every style.
            ["MelvorBase:OtherBonuses"] = PartGroup.Fight,
            ["MelvorBase:MeleeEvasion"] = PartGroup.Defence,
            ["MelvorBase:RangedEvasion"] = PartGroup.Defence,
            ["MelvorBase:MagicEvasion"] = PartGroup.Defence,
        };

    /// <summary>The equipment stats outside those categories that the search knows.</summary>
    public static IReadOnlyDictionary<string, PartGroup> OtherStats { get; } =
        new Dictionary<string, PartGroup>(StringComparer.Ordinal)
        {
            ["MelvorBase:SpellMaxHit"] = PartGroup.Fight,
            ["MelvorBase:MaximumWard"] = PartGroup.Defence,
            ["MelvorBase:Armour"] = PartGroup.Defence,
            ["MelvorBase:Lifesteal"] = PartGroup.Defence,
        };

    /// <summary>The closed sets declared on this class.</summary>
    public static IReadOnlyList<ClosedSetAttribute> ClosedSets { get; } =
    [.. typeof(KnownParts).GetCustomAttributes(typeof(ClosedSetAttribute), false).Cast<ClosedSetAttribute>()];

    /// <summary>Whether <paramref name="type"/> is a known member of the closed set named <paramref name="what"/>.</summary>
    public static bool Knows(string what, Type type) =>
        ClosedSets.Any(set => set.What == what && set.Known.Contains(type.Name, StringComparer.Ordinal));
}
