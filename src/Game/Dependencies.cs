using System.Reflection;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>One thing CS2 relies on in the game, and the feature that needs it.</summary>
/// <param name="Kind"><c>type</c>, <c>method</c>, <c>property</c>, <c>event</c>, <c>field</c>, <c>ctor</c>, <c>data</c>
/// <c>file</c> (a game resource path) or <c>patch</c> (what a Harmony patch attaches to).</param>
/// <param name="Name">
/// A type relative to <c>GamesByMalcs.Melvor2.</c> (or under <c>Godot.</c>); <c>Type.Member</c> for members;
/// <c>registry id</c> for data; for a patch, <c>Type.Method</c>, <c>Type.get_Property</c> or <c>Type..ctor</c>, with the
/// argument types' names in brackets when the name alone is ambiguous.
/// </param>
internal sealed record Dependency(string Kind, string Name, string Feature)
{
    public override string ToString() => $"{Kind} {Name}";
}

internal sealed record DependencyReport(int Resolved, int Total, IReadOnlyList<Dependency> Missing, bool DataChecked)
{
    // An index loop, not LINQ: the panel asks every frame, and a lambda over the list allocates each time.
    public bool IsAvailable(string feature)
    {
        for (var i = 0; i < Missing.Count; i++)
        {
            if (Missing[i].Feature == feature)
                return false;
        }
        return true;
    }
}

/// <summary>
/// Every game type, member, data id and patch target CS2 uses, resolved by name (<c>cs2 deps</c>). Names are strings
/// on purpose: a missing one is reported and turns its feature off. Only a missing patch target of a required feature
/// stops the mod loading, when Harmony can't attach to it.
/// </summary>
internal static partial class Dependencies
{
    private const string Ns = "GamesByMalcs.Melvor2.";
    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static DependencyReport? _last;

    /// <summary>The last report, or one with types and members only when nothing has resolved yet.</summary>
    public static DependencyReport Last => _last ??= Resolve(null);

    /// <summary>Resolves every entry; data ids only when <paramref name="game"/> is given.</summary>
    public static DependencyReport Resolve(MelvorGame? game)
    {
        var assembly = typeof(MelvorGame).Assembly;
        var missing = new List<Dependency>();
        var total = 0;
        foreach (var dep in List.Concat(Patches))
        {
            // Data ids and resource files need a running game: Load runs before the engine can answer.
            if (dep.Kind is "data" or "file" && game == null)
                continue;
            total++;
            var resolves = Resolves(dep, assembly, game);
            DevMissing(dep, ref resolves);
            if (!resolves)
                missing.Add(dep);
        }
        _last = new DependencyReport(total - missing.Count, total, missing, game != null);
        return _last;
    }

    public static bool IsAvailable(string feature) => Last.IsAvailable(feature);

    /// <summary>
    /// Reads the dev levers kept in the mod's data folder, before the first resolve; Release builds keep none.
    /// </summary>
    public static void UseLevers(string folder) => ReadDevMiss(folder);

    // Dev builds only: a lever makes one entry read as missing, to see what its feature does without it.
    static partial void ReadDevMiss(string folder);

    static partial void DevMissing(Dependency dep, ref bool resolves);

    /// <summary>The features a run can't do without.</summary>
    public static readonly IReadOnlySet<string> Required = new HashSet<string>(StringComparer.Ordinal)
    {
        Engine,
        Snapshot,
        Guards,
        Tally,
        Adjust,
        Targets,
    };

    /// <summary>
    /// What a result can't include because an optional feature is missing, for its "unsupported" list.
    /// </summary>
    public static IEnumerable<Note> OptionalMissing() =>
        Last
            .Missing.Where(d => !Required.Contains(d.Feature))
            .GroupBy(d => d.Feature)
            .Select(g => Note.Of("{0}: not counted, missing {1}", g.Key, string.Join(", ", g)));

    private static bool Resolves(Dependency dep, Assembly assembly, MelvorGame? game)
    {
        if (dep.Kind == "data")
            return game != null && ResolvesData(dep.Name, game);
        if (dep.Kind == "type")
            return assembly.GetType(Ns + dep.Name) != null;
        if (dep.Kind == "file")
            return Godot.ResourceLoader.Exists(dep.Name);
        if (dep.Kind == "patch")
            return PatchTarget(dep.Name, assembly);
        var dot = dep.Name.LastIndexOf('.');
        var type = assembly.GetType(Ns + dep.Name[..dot]);
        if (type == null)
            return false;
        var member = dep.Name[(dot + 1)..];
        if (dep.Kind == "ctor")
            return type.GetConstructors(All).Length > 0;
        var types = new[] { type }.Concat(Bases(type)).Concat(type.GetInterfaces());
        return types.Any(t =>
            dep.Kind switch
            {
                "method" => t.GetMethods(All).Any(m => m.Name == member),
                "property" => t.GetProperty(member, All) != null,
                "event" => t.GetEvent(member, All) != null,
                "field" => t.GetField(member, All) != null,
                _ => false,
            }
        );
    }

    // As Harmony finds a target, on the declaring type only: a name alone must name one method (more is ambiguous,
    // which Harmony refuses), and argument types, where given, pick the non-generic overload by their names.
    private static bool PatchTarget(string name, Assembly assembly)
    {
        var bracket = name.IndexOf('(', StringComparison.Ordinal);
        var path = bracket < 0 ? name : name[..bracket];
        var ctor = path.EndsWith("..ctor", StringComparison.Ordinal);
        var dot = ctor ? path.Length - ".ctor".Length - 1 : path.LastIndexOf('.');
        var typeName = path[..dot];
        var type = typeName.StartsWith("Godot.", StringComparison.Ordinal)
            ? typeof(Godot.GodotObject).Assembly.GetType(typeName)
            : assembly.GetType(Ns + typeName);
        if (type == null)
            return false;
        string[]? arguments =
            bracket < 0 ? null : name[(bracket + 1)..^1].Split(", ", StringSplitOptions.RemoveEmptyEntries);
        bool Takes(MethodBase m) =>
            arguments == null || m.GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(arguments);
        if (ctor)
            return type.GetConstructors(All).Count(c => !c.IsStatic && Takes(c)) == 1;
        var member = path[(dot + 1)..];
        return type.GetMethods(All | BindingFlags.DeclaredOnly)
                .Count(m => m.Name == member && (arguments == null || !m.IsGenericMethodDefinition) && Takes(m)) == 1;
    }

    private static IEnumerable<Type> Bases(Type type)
    {
        for (var t = type.BaseType; t != null; t = t.BaseType)
            yield return t;
    }

    private static bool ResolvesData(string name, MelvorGame game)
    {
        var space = name.IndexOf(' ', StringComparison.Ordinal);
        var registry = name[..space];
        var id = name[(space + 1)..];
        return registry switch
        {
            "entity" => game.Entities.TryGetObjectById(id, out _),
            _ => false,
        };
    }
}
