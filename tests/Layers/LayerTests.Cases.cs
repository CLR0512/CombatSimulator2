using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.LayerScan;

namespace MelvorMods.CombatSimulator2.Tests;

// Synthetic cases: each rule fails on a made-up source that breaks it.
public sealed partial class LayerTests
{
    [Fact]
    public void FindsUnlistedAndStaleLongFiles()
    {
        static string Lines(int n) =>
            "namespace MelvorMods.CombatSimulator2.Model;\n" + string.Concat(Enumerable.Repeat("// line\n", n - 1));
        var files = new[]
        {
            Parse("Model/Long.cs", Lines(501)),
            Parse("Model/Listed.cs", Lines(600)),
            Parse("Model/Short.cs", Lines(500)),
        };
        (string, string)[] listed = [("Model/Listed.cs", "one table"), ("Model/Short.cs", "x"), ("Model/Gone.cs", "x")];
        Assert.Equal(
            [
                "Model/Gone.cs: in the long-file list but gone",
                "Model/Long.cs: 501 lines, over 500 and not in the long-file list",
                "Model/Short.cs: in the long-file list but 500 lines",
            ],
            SizeProblems(files, listed)
        );
    }

    [Fact]
    public void FindsLongTypesOverTheirParts()
    {
        static string Part(string type, int n) =>
            "namespace MelvorMods.CombatSimulator2.Model;\npartial class "
            + type
            + "\n{\n"
            + string.Concat(Enumerable.Repeat("    // line\n", n - 3))
            + "}\n";
        var files = new[]
        {
            Parse("Model/Split.cs", Part("Split", 300)),
            Parse("Model/Split.More.cs", Part("Split", 201)),
            Parse("Model/Grown.cs", Part("Grown", 400)),
            Parse("Model/Grown.More.cs", Part("Grown", 301)),
            Parse("Model/Listed.cs", Part("Listed", 400)),
            Parse("Model/Listed.More.cs", Part("Listed", 300)),
            Parse("Model/Small.cs", Part("Small", 250)),
            Parse("Model/Small.More.cs", Part("Small", 250)),
            Parse("Panel/Small.cs", Part("Small", 400)),
        };
        (string, int, string)[] listed =
        [
            ("Model/Grown", 700, "x"),
            ("Model/Listed", 700, "one table"),
            ("Model/Small", 600, "x"),
            ("Model/Gone", 600, "x"),
        ];
        Assert.Equal(
            [
                "Model/Gone: in the long-type list but gone",
                "Model/Grown: 701 lines over its parts, over the 700 its entry allows",
                "Model/Small: in the long-type list but 500 lines",
                "Model/Split: 501 lines over its parts, over 500 and not in the long-type list",
            ],
            TypeSizeProblems(files, listed)
        );
    }

    [Fact]
    public void FindsUnlistedAndStaleHarmonyFiles()
    {
        var config = "[mods/**/*.cs]\nx = 1\n\n[mods/CombatSimulator2/src/{Game/Listed,Game/Plain}.cs]\nx = none\n";
        Assert.Equal(["Game/Listed.cs", "Game/Plain.cs"], HarmonyFiles(config));
        var files = new[]
        {
            Parse(
                "Game/Listed.cs",
                "using HarmonyLib; namespace MelvorMods.CombatSimulator2.Game; [HarmonyPatch] static class P { }"
            ),
            Parse("Game/Plain.cs", "namespace MelvorMods.CombatSimulator2.Game; static class Q { }"),
            Parse(
                "Core/New.cs",
                "namespace MelvorMods.Core; [HarmonyLib.HarmonyPatch(typeof(object), \"M\")] static class R { }"
            ),
            Parse("Game/Doc.cs", "namespace MelvorMods.CombatSimulator2.Game; /// <c>[HarmonyPatch]</c>\nclass S { }"),
        };
        Assert.Equal(
            [
                "Core/New.cs: declares [HarmonyPatch] but isn't in .editorconfig's Harmony section",
                "Game/Plain.cs: in .editorconfig's Harmony section but declares no [HarmonyPatch]",
            ],
            HarmonyProblems(files, HarmonyFiles(config))
        );
    }

    [Fact]
    public void FindsDevCodeOutsideDevFiles()
    {
        var files = new[]
        {
            Parse("Engine/Dev/Probe.cs", "namespace MelvorMods.CombatSimulator2.Engine.Dev; static class Probe { }"),
            Parse("Engine/Run.cs", "namespace MelvorMods.CombatSimulator2.Engine; partial class Run { }"),
            Parse(
                "Engine/Run.Dev.cs",
                "namespace MelvorMods.CombatSimulator2.Engine; partial class Run { void Lever() { } }"
            ),
            Parse(
                "Engine/UsesDevType.cs",
                "namespace MelvorMods.CombatSimulator2.Engine; class A { object? X = Dev.Probe.Y; Probe? P; }"
            ),
            Parse(
                "Engine/ImportsDev.cs",
                "using MelvorMods.CombatSimulator2.Engine.Dev; namespace MelvorMods.CombatSimulator2.Engine; class B { }"
            ),
            Parse(
                "Engine/InlineOk.cs",
                "namespace MelvorMods.CombatSimulator2.Engine; class C {\n#if DEBUG\n Probe? P;\n#endif\n}"
            ),
            Parse(
                "Engine/InlineNotListed.cs",
                "namespace MelvorMods.CombatSimulator2.Engine; class D {\n#if DEBUG\n#endif\n}"
            ),
            Parse(
                "Engine/Dev/Wrapped.cs",
                "#if DEBUG\nnamespace MelvorMods.CombatSimulator2.Engine.Dev; static class W { }\n#endif"
            ),
        };
        (string, string)[] listed = [("Engine/InlineOk.cs", "a hot path"), ("Engine/Gone.cs", "x")];
        Assert.Equal(
            [
                "Engine/Dev/Wrapped.cs: #if DEBUG in a dev file, which Release builds leave out already",
                "Engine/Gone.cs: in the inline-DEBUG list but gone",
                "Engine/ImportsDev.cs: imports MelvorMods.CombatSimulator2.Engine.Dev",
                "Engine/InlineNotListed.cs: #if DEBUG outside a dev file, not in the inline-DEBUG list",
                "Engine/UsesDevType.cs: names dev type Probe",
            ],
            DevProblems(files, listed)
        );
    }

    [Fact]
    public void FindsQualifiedAndDebugOnlyUses()
    {
        var files = new[]
        {
            Parse(
                "Engine/MainThread.cs",
                "namespace MelvorMods.CombatSimulator2.Engine; public static class MainThread { }"
            ),
            Parse(
                "Game/G.cs",
                "namespace MelvorMods.CombatSimulator2.Game; public static class G"
                    + " { static void F() => Engine.MainThread.Post(); }"
            ),
            Parse(
                "Model/D.cs",
                "#if DEBUG\nnamespace MelvorMods.CombatSimulator2.Model; public static class D"
                    + " { static object? X = MelvorMods.CombatSimulator2.Engine.MainThread.Y; }\n#endif"
            ),
        };
        Assert.Equal([("Game/G.cs", "Game→Engine"), ("Model/D.cs", "Model→Engine")], Violations(files));
    }

    [Fact]
    public void FindsAnUpwardUse()
    {
        var files = new[]
        {
            Parse("Model/Thing.cs", "namespace MelvorMods.CombatSimulator2.Model; public sealed class Thing { }"),
            Parse(
                "Panel/View.cs",
                "namespace MelvorMods.CombatSimulator2.Panel; public sealed class View { public Thing? T; }"
            ),
            Parse(
                "Model/Bad.cs",
                "using MelvorMods.CombatSimulator2.Panel; namespace MelvorMods.CombatSimulator2.Model;"
                    + " public sealed class Bad { public View? V; }"
            ),
        };
        Assert.Equal([("Model/Bad.cs", "Model→Panel")], Violations(files));
    }

    [Fact]
    public void FindsGameTypesGodotAndTheRoot()
    {
        var files = new[]
        {
            Parse(
                "CombatSimulator2Mod.cs",
                "namespace MelvorMods.CombatSimulator2; public sealed class CombatSimulator2Mod { }"
            ),
            Parse(
                "Engine/A.cs",
                "namespace MelvorMods.CombatSimulator2.Engine; public sealed class A"
                    + " { object? G = GamesByMalcs.Melvor2.Game.Instance; object? M = CombatSimulator2Mod.Log; }"
            ),
            Parse("Model/B.cs", "using Godot; namespace MelvorMods.CombatSimulator2.Model; public sealed class B { }"),
            Parse("Panel/C.cs", "namespace MelvorMods.CombatSimulator2.Model; public sealed class C { }"),
        };
        Assert.Equal(
            [
                ("Engine/A.cs", "Engine→game types"),
                ("Engine/A.cs", "Engine→root"),
                ("Model/B.cs", "Model→Godot"),
                ("Panel/C.cs", "namespace MelvorMods.CombatSimulator2.Model in folder Panel"),
            ],
            Violations(files)
        );
    }

    [Fact]
    public void SubfoldersBelongToTheirLayer()
    {
        var files = new[]
        {
            Parse("Model/M.cs", "namespace MelvorMods.CombatSimulator2.Model; public sealed class M { }"),
            Parse(
                "Panel/Results/R.cs",
                "namespace MelvorMods.CombatSimulator2.Panel.Results; public sealed class R { M? X; W? Y; }"
            ),
            Parse(
                "Panel/Widgets/W.cs",
                "namespace MelvorMods.CombatSimulator2.Panel.Widgets; public sealed class W { }"
            ),
            Parse("Panel/Widgets/Bad.cs", "namespace MelvorMods.CombatSimulator2.Panel; public sealed class Bad { }"),
            Parse(
                "Model/Up.cs",
                "using MelvorMods.CombatSimulator2.Panel.Widgets; namespace MelvorMods.CombatSimulator2.Model;"
                    + " public sealed class Up { W? Z; }"
            ),
        };
        Assert.Equal(
            [
                ("Model/Up.cs", "Model→Panel"),
                ("Panel/Widgets/Bad.cs", "namespace MelvorMods.CombatSimulator2.Panel in folder Panel/Widgets"),
            ],
            Violations(files)
        );
    }

    [Fact]
    public void LimitsPanelToTheRunSurface()
    {
        var files = new[]
        {
            Parse(
                "Engine/E.cs",
                "namespace MelvorMods.CombatSimulator2.Engine; public sealed class RunService { }"
                    + " public sealed class CopyPool { }"
            ),
            Parse("Host/H.cs", "namespace MelvorMods.CombatSimulator2.Host; public sealed class HostClient { }"),
            Parse(
                "Panel/P.cs",
                "using MelvorMods.CombatSimulator2.Engine; using MelvorMods.CombatSimulator2.Host;"
                    + " namespace MelvorMods.CombatSimulator2.Panel; public sealed class P"
                    + " { RunService? R; CopyPool? C; HostClient? H; }"
            ),
        };
        Assert.Equal([("Panel/P.cs", "Panel→Engine.CopyPool"), ("Panel/P.cs", "Panel→Host")], Violations(files));
    }

    [Fact]
    public void IgnoresMembersNamedLikeTypes()
    {
        var files = new[]
        {
            Parse("Game/Targets.cs", "namespace MelvorMods.CombatSimulator2.Game; public static class Targets { }"),
            Parse(
                "Model/Run.cs",
                "namespace MelvorMods.CombatSimulator2.Model; public sealed class Run"
                    + " { public int Targets { get; init; } int Count() => Targets + this.Targets;"
                    + " static Run Make() => new() { Targets = 1 }; string N => nameof(Targets); }"
            ),
        };
        Assert.Empty(Violations(files));
    }

    [Fact]
    public void KeepsTheCoreFolderToItsRules()
    {
        var files = new[]
        {
            Parse("Engine/E.cs", "namespace MelvorMods.CombatSimulator2.Engine; public sealed class Runner { }"),
            Parse(
                "Core/W.cs",
                "using GamesByMalcs.Melvor2; using Godot; namespace MelvorMods.Core;"
                    + " internal sealed class Shared { Node? N; }"
            ),
            Parse(
                "Core/X.cs",
                "using MelvorMods.CombatSimulator2.Engine; namespace MelvorMods.Core; internal sealed class Up { Runner? R; }"
            ),
            Parse("Common/L.cs", "namespace MelvorMods.CombatSimulator2.Common; public static class Log { }"),
            Parse(
                "Core/Z.cs",
                "namespace MelvorMods.Core; internal static class Own { static void F() => Log.Write(); }"
            ),
            Parse("Model/M.cs", "namespace MelvorMods.CombatSimulator2.Model; public sealed class M { Shared? S; }"),
            Parse("Panel/P.cs", "namespace MelvorMods.CombatSimulator2.Panel; public sealed class P { Shared? S; }"),
            Parse("Core/Y.cs", "namespace MelvorMods.CombatSimulator2.Core; internal sealed class Y { }"),
        };
        Assert.Equal(
            [
                ("Core/X.cs", "Core→Engine"),
                ("Core/Y.cs", "namespace MelvorMods.CombatSimulator2.Core in folder Core"),
                ("Model/M.cs", "Model→Core"),
            ],
            Violations(files)
        );
    }

    [Fact]
    public void FindsDuplicateTypeNames()
    {
        var files = new[]
        {
            Parse("Model/A.cs", "namespace MelvorMods.CombatSimulator2.Model; public sealed class Same { }"),
            Parse("Game/B.cs", "namespace MelvorMods.CombatSimulator2.Game; public sealed class Same { }"),
            Parse("Game/C.cs", "namespace MelvorMods.CombatSimulator2.Game; public partial class Split { }"),
            Parse("Game/D.cs", "namespace MelvorMods.CombatSimulator2.Game; public partial class Split { }"),
        };
        Assert.Equal([("Game/B.cs", "duplicate type Same"), ("Model/A.cs", "duplicate type Same")], Violations(files));
    }
}
