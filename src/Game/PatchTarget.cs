using System.Reflection;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Names a patch's target where Harmony's own attribute can't: a method whose generic overload takes the same
/// argument types, which Harmony reports as ambiguous. The patch class carries a bare <c>[HarmonyPatch]</c> and this
/// attribute, and its <c>TargetMethod</c> returns <see cref="Resolve"/>: the one non-generic overload named here.
/// <c>./mm check</c> reads this attribute by name and checks the same overload against each game build.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class PatchTargetAttribute(Type declaring, string method, params Type[] arguments) : Attribute
{
    private Type Declaring { get; } = declaring;
    public string Method { get; } = method;
    public IReadOnlyList<Type> Arguments { get; } = arguments;

    /// <summary>
    /// The non-generic method that <paramref name="patch"/>'s attribute names; throws if there isn't one.
    /// </summary>
    public static MethodInfo Resolve(Type patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var target =
            patch.GetCustomAttribute<PatchTargetAttribute>()
            ?? throw new InvalidOperationException($"{patch.Name} has no [PatchTarget]");
        return target
                .Declaring.GetMethods(
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Static
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly
                )
                .SingleOrDefault(m =>
                    m.Name == target.Method
                    && !m.IsGenericMethodDefinition
                    && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(target.Arguments)
                )
            ?? throw new InvalidOperationException(
                $"{target.Declaring.Name}.{target.Method}({string.Join(", ", target.Arguments.Select(a => a.Name))}) not found"
            );
    }
}
