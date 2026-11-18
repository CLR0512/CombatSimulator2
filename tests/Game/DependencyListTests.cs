using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>
/// Every target CS2's Harmony patches attach to is in the dependency list, so <c>cs2 deps</c> and the load can tell
/// a missing one before Harmony fails on it. Read from the sources: the patches name game types the tests can't load.
/// </summary>
public sealed class DependencyListTests
{
    private static readonly Dictionary<string, string> Keywords = new(StringComparer.Ordinal)
    {
        ["string"] = "String",
        ["bool"] = "Boolean",
        ["int"] = "Int32",
        ["long"] = "Int64",
        ["float"] = "Single",
        ["double"] = "Double",
        ["object"] = "Object",
    };

    private static string GameFolder => Path.Combine(TestPaths.ModFolder(), "src", "Game");

    [Fact]
    public void EveryPatchTargetIsListedAndNothingElse()
    {
        var patched = PatchedTargets();
        var listed = ListedTargets();
        Assert.NotEmpty(patched);
        Assert.Empty(patched.Keys.Except(listed));
        Assert.Empty(listed.Except(patched.Keys));
    }

    // The patches that keep the live game safe or a copy's results right fail the load when missing; only the
    // requirements switch may be left out.
    [Fact]
    public void OnlyTheRequirementsPatchIsOptional()
    {
        var optional = PatchedTargets().Where(p => p.Value).Select(p => p.Key).ToList();
        Assert.Equal(["ConditionGroup.get_AreMet"], optional);
    }

    // Type.Member(arguments) of each [HarmonyPatch] class under Game/, with whether it has a Prepare.
    private static Dictionary<string, bool> PatchedTargets()
    {
        var targets = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(GameFolder, "*.cs"))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var attributes = type.AttributeLists.SelectMany(l => l.Attributes).ToList();
                if (!attributes.Any(a => a.Name.ToString() == "HarmonyPatch"))
                    continue;
                var key = attributes
                    .Where(a => a.Name.ToString() is "HarmonyPatch" or "PatchTarget")
                    .Select(Target)
                    .OfType<string>()
                    .Single();
                var prepares = type.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "Prepare");
                targets.Add(key, prepares);
            }
        }
        return targets;
    }

    private static string? Target(AttributeSyntax attribute)
    {
        var arguments = attribute.ArgumentList?.Arguments.Select(a => a.Expression).ToList() ?? [];
        if (arguments.Count == 0)
            return null;
        var declaring = TypeName(((TypeOfExpressionSyntax)arguments[0]).Type);
        string? member = null;
        var kind = "";
        var types = new List<string>();
        foreach (var argument in arguments.Skip(1))
        {
            switch (argument)
            {
                case InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } } n:
                    member = n.ArgumentList.Arguments[0].Expression.ToString().Split('.')[^1];
                    break;
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    member = literal.Token.ValueText;
                    break;
                case MemberAccessExpressionSyntax access when access.Expression.ToString() == "MethodType":
                    kind = access.Name.Identifier.Text;
                    break;
                case CollectionExpressionSyntax collection:
                    types.AddRange(
                        collection
                            .Elements.Cast<ExpressionElementSyntax>()
                            .Select(e => TypeName(((TypeOfExpressionSyntax)e.Expression).Type))
                    );
                    break;
                case TypeOfExpressionSyntax typeOf:
                    types.Add(TypeName(typeOf.Type));
                    break;
                default:
                    Assert.Fail($"an argument the test doesn't read: {argument}");
                    break;
            }
        }
        var name = kind switch
        {
            "Constructor" => ".ctor",
            "Getter" => "get_" + member,
            "Setter" => "set_" + member,
            _ => member,
        };
        return $"{declaring}.{name}" + (types.Count > 0 ? $"({string.Join(", ", types)})" : "");
    }

    // The name reflection gives the type: the last part, generic types with their arity.
    private static string TypeName(TypeSyntax type) =>
        type switch
        {
            PredefinedTypeSyntax p => Keywords[p.Keyword.Text],
            GenericNameSyntax g => $"{g.Identifier.Text}`{g.TypeArgumentList.Arguments.Count}",
            QualifiedNameSyntax q => TypeName(q.Right),
            _ => type.ToString(),
        };

    // Type.Member(arguments) of each P(feature, type, members…) entry, the type by its last part.
    private static HashSet<string> ListedTargets()
    {
        var root = CSharpSyntaxTree
            .ParseText(File.ReadAllText(Path.Combine(GameFolder, "DependencyList.Patches.cs")))
            .GetRoot();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var call in root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(i => i.Expression.ToString() == "P")
        )
        {
            var words = call
                .ArgumentList.Arguments.Skip(1)
                .Select(a => ((LiteralExpressionSyntax)a.Expression).Token.ValueText)
                .ToList();
            var type = words[0].Split('.')[^1];
            foreach (var member in words.Skip(1))
                Assert.True(listed.Add($"{type}.{member}"), $"listed twice: {type}.{member}");
        }
        return listed;
    }
}
