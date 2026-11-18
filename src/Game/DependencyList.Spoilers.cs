namespace MelvorMods.CombatSimulator2.Game;

// What Spoiler guard reads of the live character: which places it can enter, who is fought there, and which enemies it
// has killed. Without them nothing is hidden; names and results are untouched.
internal static partial class Dependencies
{
    public const string SpoilerGuard = "spoiler-guard";

    private static IEnumerable<Dependency> SpoilerGuardRows =>
        [
            .. T(
                SpoilerGuard,
                "Components.CombatAreas.BaseCombatArea",
                "property:UnlockRequirements",
                "property:DisplayRequirements",
                "property:DisplayName"
            ),
            .. T(SpoilerGuard, "Components.CombatAreas.CombatArea", "property:Enemies"),
            .. T(SpoilerGuard, "Components.CombatAreas.Dungeon", "property:Enemies"),
            .. T(SpoilerGuard, "Components.CombatAreas.DungeonEnemies", "property:Enemies"),
            .. T(SpoilerGuard, "Random.IReadOnlyWeightedTable`1", "property:Items"),
            .. T(SpoilerGuard, "Components.CombatAreas.WeightedEnemy", "property:Enemy"),
            .. T(SpoilerGuard, "Conditions.ConditionGroup", "property:AreMet"),
            .. T(SpoilerGuard, "Entities.Combat.CombatEntity", "property:Statistics"),
            .. T(SpoilerGuard, "Entities.Combat.Statistics.CombatStatistics", "property:EnemyKilled"),
            .. T(SpoilerGuard, "Components.Statistics.DictionaryStatistic`1", "GetValue"),
        ];
}
