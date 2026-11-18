using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>
/// Reads CS2's sources as syntax for the layer test: which layer each file is in, the namespace its folder
/// gives it, the types it declares and the names in it that refer to types.
/// </summary>
internal static class LayerScan
{
    public const string Prefix = "MelvorMods.CombatSimulator2";
    public const string Root = "root";

    // The shared code's CS2-only parts, kept in CS2's tree: in the shared namespace, below every
    // layer but Model, and free to use game types and Godot but none of CS2's layers.
    public const string CoreFolder = "Core";
    public const string CoreNamespace = "MelvorMods.Core";

    /// <summary>
    /// A source as a Debug build reads it (<see cref="Unit"/>), and as a Release build does (<see cref="Release"/>),
    /// where <c>#if DEBUG</c> regions are disabled text.
    /// </summary>
    public sealed record Source(
        string File,
        string Layer,
        CompilationUnitSyntax Unit,
        int Lines,
        CompilationUnitSyntax Release
    )
    {
        /// <summary>Whether this is dev code: in a <c>Dev/</c> folder, or a <c>*.Dev.cs</c> partial part.</summary>
        public bool IsDev =>
            File.Contains("/Dev/", StringComparison.Ordinal) || File.EndsWith(".Dev.cs", StringComparison.Ordinal);
    }

    // Debug-only code (dev commands, checks) is parsed too: it ships in dev builds.
    private static readonly CSharpParseOptions Options = CSharpParseOptions.Default.WithPreprocessorSymbols("DEBUG");

    public static Source Parse(string file, string text) =>
        new(
            file,
            LayerOf(file),
            (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text, Options).GetRoot(),
            text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0),
            (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text, CSharpParseOptions.Default).GetRoot()
        );

    // The folder a file sits in is its layer; files at the top are the root.
    public static string LayerOf(string file)
    {
        var slash = file.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? Root : file[..slash];
    }

    public static string SourceDir() => Path.Combine(TestPaths.ModFolder(), "src");

    public static List<Source> Sources()
    {
        var dir = SourceDir();
        var files = Directory
            .EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
            .Where(f =>
                !f.StartsWith("obj/", StringComparison.Ordinal) && !f.StartsWith("bin/", StringComparison.Ordinal)
            )
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);
        return files.Select(f => Parse(f, File.ReadAllText(Path.Combine(dir, f)))).ToList();
    }

    // The CS2 layers a file imports with a using (MelvorMods.CombatSimulator2.Engine → Engine).
    public static HashSet<string> UsedLayers(CompilationUnitSyntax unit) =>
        unit.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(u => u.NamespaceOrType.ToString())
            .Where(n => n.StartsWith(Prefix + ".", StringComparison.Ordinal))
            .Select(n => n[(Prefix.Length + 1)..].Split('.')[0])
            .ToHashSet(StringComparer.Ordinal);

    // A file's namespace follows its folders: Panel/Results/RankList.cs is in CombatSimulator2.Panel.Results.
    // Files in Core/ keep the shared code's namespace.
    public static string ExpectedNamespace(Source source)
    {
        var folder = FolderOf(source.File);
        return source.Layer == CoreFolder ? CoreNamespace
            : folder == null ? Prefix
            : $"{Prefix}.{folder.Replace('/', '.')}";
    }

    public static string? FolderOf(string file)
    {
        var slash = file.LastIndexOf('/');
        return slash < 0 ? null : file[..slash];
    }

    public static bool IsTopLevelType(MemberDeclarationSyntax member) =>
        member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
        && member.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax;

    // The lines each namespace-level type takes over all its parts, by folder and name (Engine/RunService).
    // Parts of one type share a folder, since a file's namespace follows its folder.
    public static Dictionary<string, int> TypeLines(IReadOnlyList<Source> sources)
    {
        var lines = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var folder = FolderOf(source.File);
            foreach (
                var type in source.Unit.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Where(IsTopLevelType)
            )
            {
                var name = type.Identifier.ValueText;
                if (type is TypeDeclarationSyntax { TypeParameterList.Parameters.Count: > 0 and var arity })
                    name += "`" + arity;
                var key = folder == null ? name : $"{folder}/{name}";
                var span = type.GetLocation().GetLineSpan();
                lines[key] = lines.GetValueOrDefault(key) + span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            }
        }
        return lines;
    }

    public static string TypeName(MemberDeclarationSyntax member) =>
        member switch
        {
            BaseTypeDeclarationSyntax t => t.Identifier.ValueText,
            DelegateDeclarationSyntax d => d.Identifier.ValueText,
            _ => throw new InvalidOperationException(),
        };

    // Names a file gives to its own members, parameters and locals: a simple name equal to one of
    // these is taken as that member, not as a type of another folder.
    public static HashSet<string> DeclaredMemberNames(CompilationUnitSyntax unit)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in unit.DescendantNodes())
        {
            switch (node)
            {
                case PropertyDeclarationSyntax p:
                    names.Add(p.Identifier.ValueText);
                    break;
                case MethodDeclarationSyntax m:
                    names.Add(m.Identifier.ValueText);
                    break;
                case EventDeclarationSyntax e:
                    names.Add(e.Identifier.ValueText);
                    break;
                case VariableDeclaratorSyntax v:
                    names.Add(v.Identifier.ValueText);
                    break;
                case ParameterSyntax p:
                    names.Add(p.Identifier.ValueText);
                    break;
                case EnumMemberDeclarationSyntax e:
                    names.Add(e.Identifier.ValueText);
                    break;
                case SingleVariableDesignationSyntax d:
                    names.Add(d.Identifier.ValueText);
                    break;
            }
        }
        return names;
    }

    // Whether a simple name in code refers to a type, as far as syntax can tell: not a member
    // accessed on something, not a named argument or initializer target, not inside nameof, and
    // not one of the file's own member names unless it is plainly in a type position. A name
    // after a dot is a type only when what's before it is a namespace: CS2's own, or a layer name.
    public static bool NamesAType(SimpleNameSyntax name, HashSet<string> own, IReadOnlySet<string> layers)
    {
        switch (name.Parent)
        {
            case MemberAccessExpressionSyntax access when access.Name == name:
                var before = access.Expression.ToString();
                return before.StartsWith(Prefix, StringComparison.Ordinal) || layers.Contains(before);
            case MemberBindingExpressionSyntax:
            case NameColonSyntax:
            case NameEqualsSyntax:
                return false;
            case AssignmentExpressionSyntax assign
                when assign.Left == name && assign.Parent is InitializerExpressionSyntax:
                return false;
        }
        if (
            name.Ancestors()
                .OfType<InvocationExpressionSyntax>()
                .Any(i => i.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" })
        )
            return false;
        if (own.Contains(name.Identifier.ValueText) && !InTypePosition(name))
            return false;
        return true;
    }

    private static bool InTypePosition(SimpleNameSyntax name) =>
        name.Parent
            is VariableDeclarationSyntax
                or ObjectCreationExpressionSyntax
                or TypeArgumentListSyntax
                or BaseTypeSyntax
                or ParameterSyntax
                or PropertyDeclarationSyntax
                or MethodDeclarationSyntax
                or NullableTypeSyntax
                or ArrayTypeSyntax
                or CastExpressionSyntax
                or TypeOfExpressionSyntax
                or DeclarationPatternSyntax
                or TypePatternSyntax;
}
