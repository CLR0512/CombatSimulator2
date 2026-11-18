namespace MelvorMods.CombatSimulator2.Game;

// What CS2's Harmony patches attach to. A guard, the resource sharing between copies, the one-at-a-time plural
// lookups, or the conditions a copy's reset brings up to date missing means CS2 can't keep the live game safe or a
// copy's results right, so they name a required feature and their patches stay without a Prepare: a missing target fails the load and every patch already
// applied is removed. Ignoring requirements is a feature of its own, switched off when its target is missing.
internal static partial class Dependencies
{
    public const string IgnoreRequirements = "ignore-requirements";

    private static IEnumerable<Dependency> P(string feature, string type, params string[] members) =>
        members.Select(member => new Dependency("patch", $"{type}.{member}", feature));

    public static readonly IReadOnlyList<Dependency> Patches =
    [
        .. P(Guards, "Audio.AudioManager", "SetPlaylist", "PlayNextTrack", "ForcePlayTrack"),
        .. P(
            Guards,
            "Entities.TimeAndWeather.TimeOfDayComponent",
            ".ctor(IReadOnlyPriorityList`1, GameEntity, ICoreSystems, String)"
        ),
        .. P(
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
        .. P(Guards, "Settings.LocalSettingsManager", "SaveUserSettings", "SaveDeviceSettings"),
        .. P(
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
        .. P(Guards, "Entities.Combat.CombatActionComponent", "PlayerDiedHandler"),
        .. P(
            Engine,
            "Godot.ResourceLoader",
            "LoadThreadedGet",
            "LoadThreadedRequest",
            "Load(String, String, CacheMode)"
        ),
        .. P(Engine, "Godot.TranslationServer", "TranslatePlural"),
        .. P(Engine, "Conditions.Condition", ".ctor(DataManager, JsonElement)", ".ctor(Boolean)"),
        .. P(IgnoreRequirements, "Conditions.ConditionGroup", "get_AreMet"),
    ];
}
