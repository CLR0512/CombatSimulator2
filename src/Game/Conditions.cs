using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Data;
using HarmonyLib;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The conditions a copy's game made, so a reset can bring their met state up to date. A condition that something
/// listens to caches whether it is met and only checks again on its progress events; reading a component back from
/// its save raises none, so after a reset a requirement can stay met (or unmet) when the copy's state says otherwise.
/// </summary>
internal sealed partial class CopyConditions
{
    private readonly List<Condition> _all = [];

    [ThreadStatic]
    private static CopyConditions? _collecting;

    /// <summary>Collects the conditions made on this thread into <paramref name="into"/> until disposed.</summary>
    public static Collecting Collect(CopyConditions into) => new(into);

    internal static void Made(Condition condition) => _collecting?._all.Add(condition);

    /// <summary>
    /// Checks every listening condition again through the game's own update, which raises the events real progress
    /// would (bonuses added or removed, tasks moving), until none changes. Returns the passes it took.
    /// </summary>
    public int Refresh()
    {
        for (var pass = 1; pass <= 5; pass++)
        {
            var changed = false;
            foreach (var condition in _all)
            {
                var access = Access.For(condition.GetType());
                if (access == null || !access.IsListening(condition))
                    continue;
                var before = access.CachedMet(condition);
                access.Update(condition);
                changed |= access.CachedMet(condition) != before;
            }
            if (!changed)
                return pass;
        }
        return 5;
    }

    /// <summary>A hash of every listening condition's cached met state, in the order the game made them.</summary>
    public string Hash()
    {
        var text = new StringBuilder();
        for (var i = 0; i < _all.Count; i++)
        {
            var access = Access.For(_all[i].GetType());
            if (access != null && access.IsListening(_all[i]))
                text.Append(i).Append(access.CachedMet(_all[i]) ? '+' : '-');
        }
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))
        )[..16];
    }

    public readonly struct Collecting : IDisposable
    {
        private readonly CopyConditions? _previous;

        public Collecting(CopyConditions into)
        {
            _previous = _collecting;
            _collecting = into;
        }

        public void Dispose() => _collecting = _previous;
    }

    // The listening flag, the cached met state and the update of one EventCondition<T>, compiled once per type.
    private sealed class Access(Func<object, bool> listening, Func<object, bool> met, Action<object> update)
    {
        private static readonly ConditionalWeakTable<Type, Access?> _byType = [];

        public bool IsListening(object condition) => listening(condition);

        public bool CachedMet(object condition) => met(condition);

        public void Update(object condition) => update(condition);

        public static Access? For(Type type) => _byType.GetValue(type, Make);

        private static Access? Make(Type type)
        {
            var baseType = type;
            while (
                baseType != null
                && !(baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(EventCondition<>))
            )
                baseType = baseType.BaseType;
            if (baseType == null)
                return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var listening = baseType.GetField("_isListening", flags);
            var met = baseType.GetField("_isMet", flags);
            var update = baseType.GetMethod("UpdateIsMet", flags, Type.EmptyTypes);
            if (listening == null || met == null || update == null)
                return null;
            var instance = Expression.Parameter(typeof(object));
            var typed = Expression.Convert(instance, baseType);
            return new Access(
                Expression.Lambda<Func<object, bool>>(Expression.Field(typed, listening), instance).Compile(),
                Expression.Lambda<Func<object, bool>>(Expression.Field(typed, met), instance).Compile(),
                Expression.Lambda<Action<object>>(Expression.Call(typed, update), instance).Compile()
            );
        }
    }
}

// Every condition the game makes, from data or in code, passes through one of these constructors.

[HarmonyPatch(typeof(Condition), MethodType.Constructor, [typeof(DataManager), typeof(JsonElement)])]
internal static class CollectDataConditions
{
    private static void Postfix(Condition __instance) => CopyConditions.Made(__instance);
}

[HarmonyPatch(typeof(Condition), MethodType.Constructor, [typeof(bool)])]
internal static class CollectCodeConditions
{
    private static void Postfix(Condition __instance) => CopyConditions.Made(__instance);
}
