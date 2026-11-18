using System.Collections.Concurrent;
using System.Reflection;
using GamesByMalcs.Melvor2.Components.Experience;
using Godot;
using HarmonyLib;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Godot resources a copy's game fetches come from Godot's resource cache, so every copy in the process gets the
/// same objects: icons, sounds and scenes while it loads its data (through
/// <see cref="ResourceLoader.LoadThreadedGet"/>), and icons the game loads while it plays (through
/// <c>ResourceLoader.Load</c>, which <c>GD.Load</c> calls), such as an event-log entry's icon when a fight moves a
/// guild task on. Godot's C# bridge isn't safe when two threads touch one of them at once: making its managed wrapper
/// again after the old one was collected stops the whole process ("gchandle ... != p_old_gchandle"), and so can the
/// swap between a weak and a strong handle it makes whenever the object's reference count crosses one. Both happen
/// when copies are built side by side after the copies that held a resource were collected. So on simulating
/// threads, the first fetch of each path goes one at a time, and what it returns is kept for the life of the process
/// with one extra native reference: its wrapper is never collected, its count never drops back to one, and later
/// fetches of the path need no lock. Other threads load as before.
/// </summary>
internal static class SharedResources
{
    private static readonly Lock _lock = new();
    private static readonly HashSet<Resource> _kept = new(ReferenceEqualityComparer.Instance);
    private static readonly ConcurrentDictionary<string, byte> _keptPaths = new(StringComparer.Ordinal);

    /// <summary>
    /// Takes the lock and returns true for a simulating thread's first fetch of a path not kept yet; otherwise
    /// returns false.
    /// </summary>
    internal static bool Enter(string path)
    {
        if (!Guards.Simulating || _keptPaths.ContainsKey(path))
            return false;
        _lock.Enter();
        return true;
    }

    /// <summary>Keeps what a first fetch returned, while the fetching thread still holds the lock.</summary>
    internal static void Keep(bool entered, string path, Resource? resource)
    {
        if (!entered || resource == null)
            return;
        if (_kept.Add(resource))
            _ = resource.Reference();
        _keptPaths.TryAdd(path, 0);
    }

    // Only a lock this thread holds: a finalizer left behind by an earlier build of the mod (a reload that didn't remove
    // it) is handed this build's "entered" too, as Harmony shares state by the patch's name, and must do nothing.
    internal static void Exit(bool entered)
    {
        if (entered && _lock.IsHeldByCurrentThread)
            _lock.Exit();
    }
}

[HarmonyPatch(typeof(ResourceLoader), nameof(ResourceLoader.LoadThreadedGet))]
internal static class SerializeLoadThreadedGet
{
    private static bool Prefix(string path, ref Resource? __result, out bool __state)
    {
        __state = false;
        if (HostStandIns.Get(path) is { } standIn)
        {
            __result = standIn;
            return false;
        }
        __state = SharedResources.Enter(path);
        return true;
    }

    private static void Postfix(string path, bool __state, Resource? __result) =>
        SharedResources.Keep(__state, path, __result);

    private static void Finalizer(bool __state) => SharedResources.Exit(__state);
}

[HarmonyPatch(typeof(ResourceLoader), nameof(ResourceLoader.LoadThreadedRequest))]
internal static class SkipHostResourceRequests
{
    private static bool Prefix(string path, string typeHint, ref Error __result)
    {
        if (!HostStandIns.Request(path, typeHint))
            return true;
        __result = Error.Ok;
        return false;
    }
}

// Only a cached load returns the shared object; the other cache modes give the calling thread a resource of its own.
// The generic Load<T> takes the same argument types, so the target is named with [PatchTarget].
[HarmonyPatch]
[PatchTarget(
    typeof(ResourceLoader),
    nameof(ResourceLoader.Load),
    typeof(string),
    typeof(string),
    typeof(ResourceLoader.CacheMode)
)]
internal static class SerializeLoad
{
    private static MethodInfo TargetMethod() => PatchTargetAttribute.Resolve(typeof(SerializeLoad));

    private static void Prefix(string path, ResourceLoader.CacheMode cacheMode, out bool __state) =>
        __state = cacheMode == ResourceLoader.CacheMode.Reuse && SharedResources.Enter(path);

    private static void Postfix(string path, bool __state, Resource? __result) =>
        SharedResources.Keep(__state, path, __result);

    private static void Finalizer(bool __state) => SharedResources.Exit(__state);
}

/// <summary>
/// Game caches shared by every copy in the process and filled lazily, without locks. Copies fighting at once would
/// grow them at the same moment: an exception at best, wrong cached values for the rest of the process at worst. They
/// are filled once, on one thread, before any pool starts, using the game's own methods.
/// </summary>
internal static class SharedCaches
{
    // Levels far above any reachable one; the XP for each level is cached the first time it is asked for.
    private const int Levels = 400;

    private static readonly Lock _lock = new();
    private static bool _filled;

    public static void Fill()
    {
        lock (_lock)
        {
            if (_filled)
                return;
            StandardExperienceTable.Default.GetXpForLevel(Levels);
            _filled = true;
        }
    }
}
