using System.Reflection;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.Goals;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>The game's goals, reached through their generic base.</summary>
internal static class Goals
{
    private static readonly ConditionalWeakTable<Type, MethodInfo?> _setInactive = [];

    /// <summary>Stops a goal listening for progress, through its own <c>SetInactive</c>.</summary>
    public static void SetInactive(IActiveGoal goal)
    {
        var method = _setInactive.GetValue(
            goal.GetType(),
            type =>
            {
                for (var t = type; t != null; t = t.BaseType)
                {
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ActiveGoal<,>))
                        return t.GetMethod(
                            "SetInactive",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                            Type.EmptyTypes
                        );
                }
                return null;
            }
        );
        if (method == null)
            throw new InvalidOperationException($"{goal.GetType().Name} has no SetInactive");
        method.Invoke(goal, null);
    }
}
