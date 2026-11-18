using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.AutoLoads;
using HarmonyLib;

namespace MelvorMods.Core;

// In a worker the game must not start up as a game: no Steam, saves, settings, audio, menus or cloud timers. Its
// main scene and cloud autoload return before doing anything; the scene tree itself still runs for the worker. These
// patches are applied only in the mod's own worker, by their category, before the main scene runs. The skips go
// first: a prefix that skips the original also skips the prefixes after it, and a loader may have its own.

[HarmonyPatchCategory(WorkerMode.StartupCategory)]
[HarmonyPatch(typeof(Main), nameof(Main._Ready))]
[HarmonyPriority(Priority.First)]
internal static class WorkerSkipMain
{
    private static bool Prefix()
    {
        if (WorkerMode.Current is not { } worker)
            return true;
        // The live game's language, or the game's own start-up one; model code translates names. The shared text
        // host isn't started in a worker, so this only sets the game's language.
        var locale = worker.Locale ?? "en_GB";
        ModText.SetGameLocale(locale);
        Log.Write(worker.Mod, $"worker: game start-up skipped, language {locale}");
        return false;
    }
}

[HarmonyPatchCategory(WorkerMode.StartupCategory)]
[HarmonyPatch(typeof(CloudConfigurationUpdater), nameof(CloudConfigurationUpdater._Ready))]
[HarmonyPriority(Priority.First)]
internal static class WorkerSkipCloudConfiguration
{
    private static bool Prefix() => !WorkerMode.Active;
}

// Once the scene tree is ready (the cloud autoload's _Ready, skipped above, still returns here): connect to the live
// game and hand over to the mod's worker on a thread of its own, until it returns.
[HarmonyPatchCategory(WorkerMode.StartupCategory)]
[HarmonyPatch(typeof(CloudConfigurationUpdater), nameof(CloudConfigurationUpdater._Ready))]
internal static class WorkerHost
{
    private static string? _id;
    private static string? _role;
    private static string? _directory;
    private static string? _dataRoot;
    private static int _started;

    /// <summary>
    /// What the worker needs once the tree is ready: the mod's id, the role, its folder and data root.
    /// </summary>
    // Not named Prepare: Harmony calls a patch class's Prepare itself, before patching.
    internal static void Configure(string id, string role, string directory, string dataRoot) =>
        (_id, _role, _directory, _dataRoot) = (id, role, directory, dataRoot);

    private static void Postfix(CloudConfigurationUpdater __instance)
    {
        if (
            WorkerMode.Current is not { } worker
            || _id is not { } id
            || _role is not { } role
            || Interlocked.Exchange(ref _started, 1) == 1
        )
            return;
        GameTree.Current = __instance.GetTree();
        new Thread(() => Run(worker, id, role)) { IsBackground = true, Name = $"melvormods-worker-{role}" }.Start();
    }

    private static void Run(WorkerArgs worker, string id, string role)
    {
        try
        {
            var channel = WorkerMode.Connect(worker);
            Log.Write(id, "connected to the game");
            WorkerMode.WatchParent(worker.ParentPid);
            var assembly = typeof(WorkerHost).Assembly;
            var context = new WorkerContext(
                id,
                role,
                channel,
                _directory!,
                Path.Combine(_dataRoot!, Standalone.SettingsFile),
                worker.ParentPid,
                assembly
            );
            var types = assembly
                .GetTypes()
                .Where(t => typeof(IWorkerMod).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                .ToList();
            if (types.Count != 1)
                throw new InvalidOperationException($"expected one IWorkerMod class, found {types.Count}");
            var mod = (IWorkerMod)Activator.CreateInstance(types[0])!;
            mod.RunWorker(context);
            WorkerMode.Exit(0, "the worker returned");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write(id, $"ERROR worker failed: {e}");
            WorkerMode.Exit(1, "the worker failed");
        }
    }
}
