using System.Collections.Concurrent;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Audio;
using GamesByMalcs.Melvor2.DataStructures;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Entities.TimeAndWeather;
using GamesByMalcs.Melvor2.Notifications;
using GamesByMalcs.Melvor2.Saves;
using GamesByMalcs.Melvor2.Settings;
using HarmonyLib;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Tells the game's static services apart by thread: they can't tell which <c>Game</c> calls them, but the thread
/// can, since only CS2's threads run copy code. Every patch below runs the original unless the calling thread is
/// simulating.
/// </summary>
internal static class Guards
{
    [ThreadStatic]
    private static bool _simulating;

    public static bool Simulating => _simulating;

    /// <summary>
    /// Whether this is the simulator host, where nothing of the game's should ever save: there the tripwires on
    /// settings saves block on every thread, not only simulating ones.
    /// </summary>
    public static bool InHost { get; set; }

    /// <summary>Marks the current thread as simulating until disposed; nests.</summary>
    public static SimulationScope Enter() => new(true);

    /// <summary>Whether to run the original: on a simulating thread, counts a skip and returns false.</summary>
    public static bool Skip(string guard)
    {
        if (!_simulating)
            return true;
        GuardLog.Record(guard, GuardAction.Skipped);
        return false;
    }

    /// <summary>
    /// Like <see cref="Skip"/>, for paths that should never run in a copy: also keeps the first stack.
    /// </summary>
    public static bool Block(string guard)
    {
        if (!_simulating)
            return true;
        GuardLog.Record(guard, GuardAction.Blocked);
        return false;
    }

    public readonly struct SimulationScope : IDisposable
    {
        private readonly bool _previous;
        private readonly Action? _then;

        public SimulationScope(bool simulating, Action? then = null)
        {
            _previous = _simulating;
            _simulating = simulating;
            _then = then;
        }

        public void Dispose()
        {
            _simulating = _previous;
            _then?.Invoke();
        }
    }
}

internal enum GuardAction
{
    Skipped,
    Blocked,
}

internal sealed record GuardEvent(string Guard, GuardAction Action, int Count, string? FirstStack);

/// <summary>Thread-safe guard counts, reset at the start of each run and readable since then.</summary>
internal static class GuardLog
{
    private sealed class Entry(GuardAction action)
    {
        public GuardAction Action { get; } = action;
        public int Count;
        public string? FirstStack;
    }

    private static readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    // The current target's "unsupported" list, on the thread fighting it.
    [ThreadStatic]
    private static List<Note>? _target;

    public static void Record(string guard, GuardAction action)
    {
        var entry = _entries.GetOrAdd(guard, _ => new Entry(action));
        if (Interlocked.Increment(ref entry.Count) == 1 && action == GuardAction.Blocked)
            entry.FirstStack = Environment.StackTrace;
        if (
            action == GuardAction.Blocked
            && _target != null
            && !_target.Any(u => u.English().StartsWith(guard + ":", StringComparison.Ordinal))
        )
            _target.Add(Note.Of("{0}: blocked a side effect at {1}", guard, Caller()));
    }

    /// <summary>
    /// Collects blocked guards hit on this thread into <paramref name="unsupported"/> until disposed.
    /// </summary>
    public static Guards.SimulationScope CollectFor(List<Note> unsupported)
    {
        _target = unsupported;
        return new Guards.SimulationScope(true, () => _target = null);
    }

    // The first game frame on the stack: where the side effect came from.
    private static string Caller()
    {
        var frames = new System.Diagnostics.StackTrace(2).GetFrames();
        var game = frames
            .Select(f => f.GetMethod())
            .FirstOrDefault(m =>
                m?.DeclaringType?.Namespace?.StartsWith("GamesByMalcs", StringComparison.Ordinal) == true
                && m.DeclaringType.Namespace != typeof(GamesByMalcs.Melvor2.Saves.SaveManager).Namespace
                && m.DeclaringType != typeof(GamesByMalcs.Melvor2.Notifications.NotificationManager)
            );
        return game == null ? "unknown" : $"{game.DeclaringType!.Name}.{game.Name}";
    }

    public static IReadOnlyList<GuardEvent> Snapshot() =>
        [
            .. _entries
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new GuardEvent(
                    p.Key,
                    p.Value.Action,
                    Volatile.Read(ref p.Value.Count),
                    p.Value.FirstStack
                )),
        ];

    public static IReadOnlyList<GuardEvent> Blocked => [.. Snapshot().Where(e => e.Action == GuardAction.Blocked)];

    public static void Reset() => _entries.Clear();

    /// <summary>This run's guard events, to send from the host to the live game.</summary>
    public static (Dictionary<string, int> Skipped, List<BlockedGuard> Blocked) Export()
    {
        var events = Snapshot();
        return (
            events
                .Where(e => e.Action == GuardAction.Skipped)
                .ToDictionary(e => e.Guard, e => e.Count, StringComparer.Ordinal),
            [
                .. events
                    .Where(e => e.Action == GuardAction.Blocked)
                    .Select(e => new BlockedGuard(e.Guard, e.Count, e.FirstStack)),
            ]
        );
    }

    /// <summary>
    /// Adds the host's guard events for a run to this process's log, so <c>cs2dev guards</c> shows both.
    /// </summary>
    public static void Merge(IReadOnlyDictionary<string, int> skipped, IEnumerable<BlockedGuard> blocked)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        ArgumentNullException.ThrowIfNull(blocked);
        foreach (var (guard, count) in skipped)
            Interlocked.Add(ref _entries.GetOrAdd(guard, _ => new Entry(GuardAction.Skipped)).Count, count);
        foreach (var b in blocked)
        {
            var entry = _entries.GetOrAdd(b.Name, _ => new Entry(GuardAction.Blocked));
            Interlocked.Add(ref entry.Count, b.Count);
            entry.FirstStack ??= b.Stack;
        }
    }
}

// Combat state changes set the music playlist: live music, and node access from a worker thread.

[HarmonyPatch(typeof(AudioManager), nameof(AudioManager.SetPlaylist))]
internal static class GuardSetPlaylist
{
    private static bool Prefix() => Guards.Skip("AudioManager.SetPlaylist");
}

[HarmonyPatch(typeof(AudioManager), nameof(AudioManager.PlayNextTrack))]
internal static class GuardPlayNextTrack
{
    private static bool Prefix() => Guards.Skip("AudioManager.PlayNextTrack");
}

[HarmonyPatch(typeof(AudioManager), nameof(AudioManager.ForcePlayTrack))]
internal static class GuardForcePlayTrack
{
    private static bool Prefix() => Guards.Skip("AudioManager.ForcePlayTrack");
}

// Each TimeOfDayComponent subscribes to static settings events in its constructor and never unsubscribes: every
// discarded copy would leak, and a settings change would run on the main thread against a copy being ticked.

[HarmonyPatch(
    typeof(TimeOfDayComponent),
    MethodType.Constructor,
    [typeof(IReadOnlyPriorityList<DaySegment>), typeof(GameEntity), typeof(ICoreSystems), typeof(string)]
)]
internal static class GuardTimeOfDaySettings
{
    private static void Postfix(TimeOfDayComponent __instance)
    {
        if (!Guards.Simulating)
            return;
        TimeOfDaySettings settings = LocalSettingsManager.Settings.TimeOfDay;
        settings.CycleTypeChanged -= __instance.CycleTypeChangedHandler;
        settings.SetTimeOfDayChanged -= __instance.SetTimeOfDayChangedHandler;
        Guards.Skip("TimeOfDayComponent settings subscription");
    }
}

// Saves: nothing CS2 does should reach these; they are tripwires.

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.WriteLocalSaveToActiveId))]
internal static class GuardWriteLocalSave
{
    private static bool Prefix() => Guards.Block("SaveManager.WriteLocalSaveToActiveId");
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.CreateNewActiveSaveId))]
internal static class GuardCreateNewActiveSaveId
{
    private static bool Prefix() => Guards.Block("SaveManager.CreateNewActiveSaveId");
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SetActiveSaveId))]
internal static class GuardSetActiveSaveId
{
    private static bool Prefix() => Guards.Block("SaveManager.SetActiveSaveId");
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SetActiveSaveBackup))]
internal static class GuardSetActiveSaveBackup
{
    private static bool Prefix() => Guards.Block("SaveManager.SetActiveSaveBackup");
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.ClearActiveSaveId))]
internal static class GuardClearActiveSaveId
{
    private static bool Prefix() => Guards.Block("SaveManager.ClearActiveSaveId");
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.WriteLocalActiveIdToCloud))]
internal static class GuardWriteCloud
{
    private static bool Prefix(ref Task __result) =>
        GuardTasks.Block("SaveManager.WriteLocalActiveIdToCloud", ref __result);
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.PerformPlatformCloudSync))]
internal static class GuardCloudSync
{
    private static bool Prefix(ref Task __result) =>
        GuardTasks.Block("SaveManager.PerformPlatformCloudSync", ref __result);
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.WriteSaveStringToAllSourcesAsync))]
internal static class GuardWriteSaveString
{
    private static bool Prefix(ref Task<List<SaveException>> __result) =>
        GuardTasks.Block("SaveManager.WriteSaveStringToAllSourcesAsync", ref __result);
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteSaveWithIdAsync))]
internal static class GuardDeleteSave
{
    private static bool Prefix(ref Task<List<SaveException>> __result) =>
        GuardTasks.Block("SaveManager.DeleteSaveWithIdAsync", ref __result);
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.CopySaveWithIdAsync))]
internal static class GuardCopySave
{
    private static bool Prefix(ref Task<List<SaveException>> __result) =>
        GuardTasks.Block("SaveManager.CopySaveWithIdAsync", ref __result);
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.CopySaveStringAsync))]
internal static class GuardCopySaveString
{
    private static bool Prefix(ref Task<List<SaveException>> __result) =>
        GuardTasks.Block("SaveManager.CopySaveStringAsync", ref __result);
}

internal static class GuardTasks
{
    public static bool BlockSettings(string guard)
    {
        if (!Guards.InHost)
            return Guards.Block(guard);
        GuardLog.Record(guard, GuardAction.Blocked);
        return false;
    }

    public static bool Block(string guard, ref Task result)
    {
        if (Guards.Block(guard))
            return true;
        result = Task.CompletedTask;
        return false;
    }

    public static bool Block(string guard, ref Task<List<SaveException>> result)
    {
        if (Guards.Block(guard))
            return true;
        result = Task.FromResult(new List<SaveException>());
        return false;
    }
}

// Settings saves: nothing in a copy or the simulator host should write the player's settings. In the host they are
// blocked on every thread, since the host's settings are only the defaults.

[HarmonyPatch(typeof(LocalSettingsManager), nameof(LocalSettingsManager.SaveUserSettings))]
internal static class GuardSaveUserSettings
{
    private static bool Prefix() => GuardTasks.BlockSettings("LocalSettingsManager.SaveUserSettings");
}

[HarmonyPatch(typeof(LocalSettingsManager), nameof(LocalSettingsManager.SaveDeviceSettings))]
internal static class GuardSaveDeviceSettings
{
    private static bool Prefix() => GuardTasks.BlockSettings("LocalSettingsManager.SaveDeviceSettings");
}

// Toasts and notifications: a copy's requestors reach no view, so these are tripwires too.

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddNotification))]
internal static class GuardAddNotification
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddNotification");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddItemNotification))]
internal static class GuardAddItemNotification
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddItemNotification");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddCurrencyNotification))]
internal static class GuardAddCurrencyNotification
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddCurrencyNotification");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddMusicNotification))]
internal static class GuardAddMusicNotification
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddMusicNotification");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddAnnouncement))]
internal static class GuardAddAnnouncement
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddAnnouncement");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddSkillLevelUpAnnouncement))]
internal static class GuardAddSkillLevelUpAnnouncement
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddSkillLevelUpAnnouncement");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddQuestStepCompletedNotification))]
internal static class GuardAddQuestStepCompletedNotification
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddQuestStepCompletedNotification");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddQuestTaskGoalCompletedNotification))]
internal static class GuardAddQuestTaskGoalCompletedNotification
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddQuestTaskGoalCompletedNotification");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddMasteryGuildTaskCompletedNotification))]
internal static class GuardAddMasteryGuildTaskCompletedNotification
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddMasteryGuildTaskCompletedNotification");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddMasteryGuildTaskProgressedNotification))]
internal static class GuardAddMasteryGuildTaskProgressedNotification
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddMasteryGuildTaskProgressedNotification");
}

[HarmonyPatch(typeof(NotificationManager), nameof(NotificationManager.AddNewItemFoundNotification))]
internal static class GuardAddNewItemFoundNotification
{
    private static bool Prefix() => Guards.Block("NotificationManager.AddNewItemFoundNotification");
}

// "No item loss on death": the game's handler deletes a random equipped item (or 10% of a stack), then raises
// PlayerDied and stops combat (CombatActionComponent.PlayerDiedHandler). In a copy only the item loss is skipped.

[HarmonyPatch(typeof(CombatActionComponent), "PlayerDiedHandler")]
internal static class GuardDeathItemLoss
{
    private static bool Prefix(CombatActionComponent __instance, EventHandler<PlayerDiedEventArgs>? ___PlayerDied)
    {
        if (Guards.Skip("CombatActionComponent.PlayerDiedHandler item loss"))
            return true;
        ___PlayerDied?.Invoke(__instance, new PlayerDiedEventArgs(null));
        __instance.TryStop();
        return false;
    }
}
