using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.LayerScan;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>
/// CS2's folders are layers, each allowed to use only the layers below it. This reads the mod's
/// sources as syntax (no game references needed, see <see cref="LayerScan"/>), maps every type CS2
/// declares to its folder, and checks each file's uses against the rules.
/// </summary>
public sealed partial class LayerTests
{
    // Bottom to top. A layer may use the layers listed for it; Panel's use of Engine is limited to
    // the run surface below, and the root may use everything.
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["Model"] = [],
        ["Common"] = ["Model"],
        ["Game"] = ["Common", "Model"],
        ["Engine"] = ["Common", "Model", "Game"],
        ["Host"] = ["Common", "Model", "Game", "Engine"],
        ["Panel"] = ["Common", "Model", "Game", "Engine"],
        ["Commands"] = ["Common", "Model", "Game", "Engine", "Host", "Panel"],
    };

    // The only Engine types Panel may use: it starts and watches runs, and reads one status snapshot.
    private static readonly HashSet<string> RunSurface = new(StringComparer.Ordinal)
    {
        "RunService",
        "RunRequest",
        "RunStatus",
    };

    private static readonly HashSet<string> Layers = Allowed.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>Known breaks of the rules, each with the file, the edge and why it stays.</summary>
    private static readonly (string File, string Edge, string Reason)[] Exceptions = [];

    [Fact]
    public void EveryFileKeepsToItsLayer()
    {
        var found = Violations(Sources());
        var excepted = Exceptions.Select(e => (e.File, e.Edge)).ToHashSet();
        var unexpected = found.Where(v => !excepted.Contains(v)).Select(v => $"{v.File}: {v.Edge}").ToList();
        var stale = Exceptions
            .Where(e => !found.Contains((e.File, e.Edge)))
            .Select(e => $"{e.File}: {e.Edge} (listed as an exception but no longer happens)")
            .ToList();
        Assert.True(
            unexpected.Count == 0 && stale.Count == 0,
            string.Join("\n", unexpected.Concat(stale).Order(StringComparer.Ordinal))
        );
    }

    // A file over this many lines is read as several concerns until it is split or listed with why it is one.
    private const int LongFile = 500;

    /// <summary>Files over <see cref="LongFile"/> lines, each with why it stays one file.</summary>
    private static readonly (string File, string Reason)[] LongFiles = [];

    [Fact]
    public void LongFilesAreListed()
    {
        var problems = SizeProblems(Sources(), LongFiles);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // Splitting a type into partial parts keeps each file short while the type stays as large, so a type's parts
    // are measured together.
    private const int LongType = 500;

    /// <summary>
    /// Types whose parts add up to more than <see cref="LongType"/> lines, each with the most it may reach and why
    /// it stays one type.
    /// </summary>
    private static readonly (string Type, int Most, string Reason)[] LongTypes =
    [
        (
            "Engine/CopyPool",
            610,
            "one job's threads share its stock, counters and cancel; the copies part builds them and puts on a monster's own loadout"
        ),
        (
            "Engine/RunService",
            1530,
            "the one active run, search or Best loadouts (one master, some or all): every part changes the same state on the main thread"
        ),
        (
            "Engine/Search/SearchRun",
            960,
            "one search on the main thread: its rounds, the climb, Droptimizer's stages, a round fought once, a stop and a set's weights change the same state"
        ),
        ("Host/HostServer", 710, "one pipe: runs and searches share its gate, stock, peak and cancel"),
        (
            "Game/Copy",
            510,
            "one copy of the game: building, preparing, a monster's own loadout and its reset share the game it holds"
        ),
        (
            "Game/Driver",
            600,
            "one fight's loop: its trials, trials that run over, early stops, the counts it is read at and the gaps it notes share the same bout"
        ),
        ("Game/Dependencies", 1330, "one table, a row per game name CS2 uses"),
        (
            "Model/SetupLine",
            580,
            "one format: setup, preset and simulation lines share its groups, ids, bounds and check"
        ),
        ("Panel/Cs2Modal", 700, "one modal built once: the frame, tabs and its four views' switching share its nodes"),
        ("Panel/SetupTab/GearPage", 600, "one page: its levels and loadout columns edit and redraw the same side"),
        (
            "Panel/Simulate/TargetStrip",
            560,
            "one hand-drawn control: layout, paint, clicks and hover read the same areas and portraits"
        ),
        ("Panel/Widgets/Chart", 910, "one hand-drawn control: layout, paint, toggles and hover read the same slots"),
    ];

    [Fact]
    public void LongTypesAreListed()
    {
        var problems = TypeSizeProblems(Sources(), LongTypes);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // The .editorconfig section turning the unused-code checks off for Harmony files, by name. Whatever leads to
    // the mod's folder comes before it, and differs with where the mod is.
    private const string HarmonySection = "src/{";

    [Fact]
    public void HarmonyFilesAreTheExemptOnes()
    {
        var config = File.ReadAllText(TestPaths.EditorConfig());
        var problems = HarmonyProblems(Sources(), HarmonyFiles(config));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Product files allowed an inline <c>#if DEBUG</c>, each with why a dev part can't carry it.</summary>
    private static readonly (string File, string Reason)[] InlineDebug =
    [
        ("CombatSimulator2Mod.cs", "dev registration and the dev unload lines, before the workers stop last"),
        ("Engine/CopyPool.Copies.cs", "the per-copy allocation log, on a hot path"),
        ("Game/Driver.cs", "the per-fight allocation log and drift check, on a hot path"),
    ];

    [Fact]
    public void DevCodeStaysInDevFiles()
    {
        var problems = DevProblems(Sources(), InlineDebug);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // Dev code: what a Release build compiles must not reach it, and #if DEBUG stays where it is listed.
    private static List<string> DevProblems(
        IReadOnlyList<Source> sources,
        IReadOnlyList<(string File, string Reason)> inlineDebug
    )
    {
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        var productTypes = sources
            .Where(s => !s.IsDev)
            .SelectMany(s => s.Release.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsTopLevelType))
            .Select(TypeName)
            .ToHashSet(StringComparer.Ordinal);
        var devTypes = sources
            .Where(s => s.IsDev)
            .SelectMany(s => s.Unit.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsTopLevelType))
            .Select(TypeName)
            .Where(n => !productTypes.Contains(n))
            .ToHashSet(StringComparer.Ordinal);
        var listed = inlineDebug.Select(l => l.File).ToHashSet(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var debug = HasDebugRegion(source.Unit);
            if (source.IsDev)
            {
                if (debug)
                    problems.Add($"{source.File}: #if DEBUG in a dev file, which Release builds leave out already");
                continue;
            }
            if (debug && !listed.Contains(source.File))
                problems.Add($"{source.File}: #if DEBUG outside a dev file, not in the inline-DEBUG list");
            foreach (var u in source.Release.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                var name = u.NamespaceOrType.ToString();
                if (name.EndsWith(".Dev", StringComparison.Ordinal) || name.Contains(".Dev.", StringComparison.Ordinal))
                    problems.Add($"{source.File}: imports {name}");
            }
            var own = DeclaredMemberNames(source.Release);
            foreach (var name in source.Release.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var text = name.Identifier.ValueText;
                if (
                    devTypes.Contains(text)
                    && (NamesAType(name, own, Layers) || name.Parent is MemberAccessExpressionSyntax)
                )
                    problems.Add($"{source.File}: names dev type {text}");
            }
        }
        var files = sources.ToDictionary(s => s.File, StringComparer.Ordinal);
        foreach (var (file, _) in inlineDebug)
        {
            if (!files.TryGetValue(file, out var source))
                problems.Add($"{file}: in the inline-DEBUG list but gone");
            else if (!HasDebugRegion(source.Unit))
                problems.Add($"{file}: in the inline-DEBUG list but has no #if DEBUG");
        }
        return [.. problems];
    }

    private static bool HasDebugRegion(CompilationUnitSyntax unit) =>
        unit.DescendantTrivia(descendIntoTrivia: true)
            .Select(t => t.GetStructure())
            .OfType<IfDirectiveTriviaSyntax>()
            .Any(d => d.Condition.ToString().Contains("DEBUG", StringComparison.Ordinal));

    // The files named in the Harmony section's header: [mods/CombatSimulator2/src/{Game/Guards,Core/WorkerMode}.cs].
    private static List<string> HarmonyFiles(string config)
    {
        var header = config
            .Split('\n')
            .Select(l => l.Trim())
            .Single(l =>
                l.StartsWith('[')
                && l.Contains(HarmonySection, StringComparison.Ordinal)
                && l.EndsWith("}.cs]", StringComparison.Ordinal)
            );
        var names = header.IndexOf(HarmonySection, StringComparison.Ordinal) + HarmonySection.Length;
        return header[names..^"}.cs]".Length]
            .Split(',')
            .Select(f => f.Trim() + ".cs")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> HarmonyProblems(IReadOnlyList<Source> sources, IReadOnlyList<string> listed)
    {
        var patching = sources
            .Where(s =>
                s.Unit.DescendantNodes()
                    .OfType<AttributeSyntax>()
                    .Any(a => a.Name.ToString() is "HarmonyPatch" or "HarmonyLib.HarmonyPatch")
            )
            .Select(s => s.File)
            .ToHashSet(StringComparer.Ordinal);
        var problems = patching
            .Where(f => !listed.Contains(f))
            .Select(f => $"{f}: declares [HarmonyPatch] but isn't in .editorconfig's Harmony section")
            .Concat(
                listed
                    .Where(f => !patching.Contains(f))
                    .Select(f => $"{f}: in .editorconfig's Harmony section but declares no [HarmonyPatch]")
            );
        return problems.Order(StringComparer.Ordinal).ToList();
    }

    private static List<string> SizeProblems(
        IReadOnlyList<Source> sources,
        IReadOnlyList<(string File, string Reason)> listed
    )
    {
        var lines = sources.ToDictionary(s => s.File, s => s.Lines, StringComparer.Ordinal);
        var names = listed.Select(l => l.File).ToHashSet(StringComparer.Ordinal);
        var problems = sources
            .Where(s => s.Lines > LongFile && !names.Contains(s.File))
            .Select(s => $"{s.File}: {s.Lines} lines, over {LongFile} and not in the long-file list")
            .ToList();
        foreach (var (file, _) in listed)
        {
            if (!lines.TryGetValue(file, out var n))
                problems.Add($"{file}: in the long-file list but gone");
            else if (n <= LongFile)
                problems.Add($"{file}: in the long-file list but {n} lines");
        }
        return problems.Order(StringComparer.Ordinal).ToList();
    }

    private static List<string> TypeSizeProblems(
        IReadOnlyList<Source> sources,
        IReadOnlyList<(string Type, int Most, string Reason)> listed
    )
    {
        var lines = TypeLines(sources);
        var most = listed.ToDictionary(l => l.Type, l => l.Most, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (type, n) in lines)
        {
            if (n <= LongType)
                continue;
            if (!most.TryGetValue(type, out var allowed))
                problems.Add($"{type}: {n} lines over its parts, over {LongType} and not in the long-type list");
            else if (n > allowed)
                problems.Add($"{type}: {n} lines over its parts, over the {allowed} its entry allows");
        }
        foreach (var (type, _, _) in listed)
        {
            if (!lines.TryGetValue(type, out var n))
                problems.Add($"{type}: in the long-type list but gone");
            else if (n <= LongType)
                problems.Add($"{type}: in the long-type list but {n} lines");
        }
        return problems.Order(StringComparer.Ordinal).ToList();
    }

    private static List<(string File, string Edge)> Violations(IReadOnlyList<Source> sources)
    {
        var found = new SortedSet<(string File, string Edge)>();

        // Every type CS2 declares at namespace level, and the folders declaring it.
        var declared = new Dictionary<string, List<(string File, string Layer, bool Partial)>>(StringComparer.Ordinal);
        foreach (var source in sources)
        foreach (var type in source.Unit.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsTopLevelType))
        {
            var name = TypeName(type);
            if (!declared.TryGetValue(name, out var places))
                declared[name] = places = [];
            places.Add((source.File, source.Layer, type.Modifiers.Any(SyntaxKind.PartialKeyword)));
        }
        foreach (var (name, places) in declared)
        {
            var distinct = places.Where(p => !p.Partial).Count() + (places.Any(p => p.Partial) ? 1 : 0);
            if (distinct > 1 || places.Select(p => p.Layer).Distinct().Count() > 1)
                foreach (var place in places)
                    found.Add((place.File, $"duplicate type {name}"));
        }
        var layerOf = declared
            .Where(d => d.Value.Select(p => p.Layer).Distinct().Count() == 1)
            .ToDictionary(d => d.Key, d => d.Value[0].Layer, StringComparer.Ordinal);

        foreach (var source in sources)
        {
            CheckNamespace(source, found);
            var own = DeclaredMemberNames(source.Unit);
            // Files in Core/ are in the shared namespace: they see a layer's types only through a using of it.
            var visible = source.Layer == CoreFolder ? UsedLayers(source.Unit) : null;
            foreach (var name in source.Unit.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var text = name.Identifier.ValueText;
                if (text == "MelvorGame")
                    Use(source, "game types", null, found);
                if (!layerOf.TryGetValue(text, out var target) || !NamesAType(name, own, Layers))
                    continue;
                if (visible != null && target != CoreFolder && !visible.Contains(target))
                    continue;
                Use(source, target, text, found);
            }
            foreach (var node in source.Unit.DescendantNodes())
            {
                var qualified = node switch
                {
                    UsingDirectiveSyntax u => u.NamespaceOrType.ToString(),
                    QualifiedNameSyntax q when q.Parent is not QualifiedNameSyntax => q.ToString(),
                    MemberAccessExpressionSyntax m when m.Parent is not MemberAccessExpressionSyntax => m.ToString(),
                    _ => null,
                };
                if (qualified == null)
                    continue;
                if (qualified.StartsWith("GamesByMalcs", StringComparison.Ordinal))
                    Use(source, "game types", null, found);
                else if (qualified == "Godot" || qualified.StartsWith("Godot.", StringComparison.Ordinal))
                    Use(source, "Godot", null, found);
                else if (
                    qualified == "MelvorMods.Core"
                    || qualified.StartsWith("MelvorMods.Core.", StringComparison.Ordinal)
                )
                    Use(source, "Core", null, found);
                else if (node is UsingDirectiveSyntax && qualified.StartsWith(Prefix + ".", StringComparison.Ordinal))
                {
                    var layer = qualified[(Prefix.Length + 1)..].Split('.')[0];
                    Use(source, layer, null, found);
                }
            }
        }
        return found.ToList();
    }

    // Records a use of target by source, if the rules forbid it.
    private static void Use(Source source, string target, string? type, SortedSet<(string, string)> found)
    {
        var from = source.Layer;
        if (target == from || from == Root)
            return;
        var edge = target switch
        {
            "game types" or "Godot" when from == CoreFolder => null,
            "game types" when from != "Game" => $"{from}→game types",
            "Godot" when from == "Model" => "Model→Godot",
            "Core" when from == "Model" => "Model→Core",
            Root => $"{from}→root",
            "game types" or "Godot" or "Core" => null,
            "Engine" when from == "Panel" => type == null || RunSurface.Contains(type) ? null : $"Panel→Engine.{type}",
            _ when Allowed.TryGetValue(from, out var allowed) && allowed.Contains(target) => null,
            _ => $"{from}→{target}",
        };
        if (edge != null)
            found.Add((source.File, edge));
    }

    // A file's namespace follows its folders; files in Core/ keep the shared code's namespace.
    private static void CheckNamespace(Source source, SortedSet<(string, string)> found)
    {
        var expected = ExpectedNamespace(source);
        foreach (var ns in source.Unit.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            var name = ns.Name.ToString();
            if (name != expected)
                found.Add((source.File, $"namespace {name} in folder {FolderOf(source.File) ?? Root}"));
        }
    }
}
