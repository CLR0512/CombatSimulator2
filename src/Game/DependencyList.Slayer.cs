namespace MelvorMods.CombatSimulator2.Game;

// What masters as targets read and write of the game's Slayer tasks: forcing a master's task in a copy, its pool, its
// blocks and what they cost, and the tasks a block slot waits for. Without them masters aren't listed and gear searches
// over a master are refused; monsters and dungeons are untouched.
internal static partial class Dependencies
{
    public const string SlayerMasters = "slayer-masters";

    private static IEnumerable<Dependency> SlayerMasterRows =>
        [
            .. T(SlayerMasters, "Entities.Slayer.SlayerEntity", "property:Tasks"),
            .. T(
                SlayerMasters,
                "Components.SlayerTasks.SlayerTasksComponent",
                "property:ActiveMasters",
                "field:_mastersWithTask"
            ),
            .. T(
                SlayerMasters,
                "Components.SlayerTasks.ActiveSlayerMaster",
                "field:_currentTask",
                "field:_taskLength",
                "field:_taskProgress",
                "property:Data",
                "property:Stats",
                "property:AvailableEnemies",
                "property:BlockSlots",
                "BlockEnemy",
                "UnblockEnemy",
                "IsEnemyBlocked",
                "GetCurrentTaskArea"
            ),
            .. T(SlayerMasters, "Components.SlayerTasks.SlayerTasksStats", "property:BlockCost"),
            .. T(
                SlayerMasters,
                "Components.SlayerTasks.ActiveEnemyBlockSlot",
                "property:Data",
                "property:IsUnlocked",
                "field:_isUnlocked",
                "property:BlockedEnemy"
            ),
            .. T(SlayerMasters, "Components.SlayerTasks.EnemyBlockSlot", "property:UnlockRequirements"),
            .. T(
                SlayerMasters,
                "Components.SlayerTasks.SlayerMaster",
                "property:DisplayName",
                "property:Areas",
                "property:UnlockRequirements",
                "property:BlockSlots"
            ),
            .. T(
                SlayerMasters,
                "Entities.Slayer.Statistics.SlayerMasterStatisticCondition",
                "property:Value",
                "property:Statistic",
                "property:Master"
            ),
            .. T(SlayerMasters, "Entities.Slayer.Statistics.TasksCompletedStatistic"),
            .. T(SlayerMasters, "Components.Statistics.DictionaryStatistic`1", "GetValue"),
        ];
}
