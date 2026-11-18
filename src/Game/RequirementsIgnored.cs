using GamesByMalcs.Melvor2.Conditions;
using HarmonyLib;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// "Ignore requirements" for changes: while a copy applies a change on this thread, the requirement groups the
/// scope names (an item's equipment requirements and its slot's, a prayer's unlock requirements) read as met to the
/// game's own call. Every other group, every other thread and the live game see the original.
/// </summary>
internal static class RequirementsIgnored
{
    [ThreadStatic]
    private static List<ConditionGroup>? _groups;

    /// <summary>Treats <paramref name="groups"/> as met on this thread until disposed; nests.</summary>
    public static Scope Enter(params ConditionGroup[] groups) => new(groups);

    /// <summary>True while <paramref name="group"/> is a group this simulating thread ignores.</summary>
    public static bool Covers(ConditionGroup group)
    {
        if (_groups is not { Count: > 0 } groups || !Guards.Simulating)
            return false;
        foreach (var ignored in groups)
        {
            if (ReferenceEquals(ignored, group))
                return true;
        }
        return false;
    }

    public readonly struct Scope : IDisposable
    {
        private readonly int _before;

        public Scope(ConditionGroup[] groups)
        {
            _groups ??= [];
            _before = _groups.Count;
            _groups.AddRange(groups);
        }

        public void Dispose() => _groups!.RemoveRange(_before, _groups.Count - _before);
    }
}

[HarmonyPatch(typeof(ConditionGroup), nameof(ConditionGroup.AreMet), MethodType.Getter)]
internal static class IgnoreChangeRequirements
{
    // Without its target the Ignore requirements switch is off, and CS2 runs on without this patch.
    private static bool Prepare() => Dependencies.IsAvailable(Dependencies.IgnoreRequirements);

    private static bool Prefix(ConditionGroup __instance, ref bool __result)
    {
        if (!RequirementsIgnored.Covers(__instance))
            return true;
        __result = true;
        return false;
    }
}
