using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.LayerScan;

namespace MelvorMods.CombatSimulator2.Tests;

// A failure caught and dropped without a word is lost: every catch handles a case it names, logs, or rethrows.
public sealed partial class LayerTests
{
    [Fact]
    public void NoCatchSwallowsInSilence()
    {
        var problems = EmptyCatches(Sources());
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void FindsACatchWithNoStatementAndNoComment()
    {
        var source = Parse(
            "Model/Catches.cs",
            """
            namespace MelvorMods.CombatSimulator2.Model;
            static class Catches
            {
                static void Silent() { try { } catch (System.IO.IOException) { } }
                static void Said() { try { } catch (System.IO.IOException) { /* gone already */ } }
                static void Commented()
                {
                    try { }
                    catch (System.IO.IOException)
                    {
                        // Gone already.
                    }
                }
                static void Logged() { try { } catch (System.IO.IOException e) { System.Console.WriteLine(e); } }
                static void Bare() { try { } catch { } }
            }
            """
        );
        Assert.Equal(
            [
                "Model/Catches.cs:4: a catch with no statement and no comment: say why it is handled, log it, or rethrow",
                "Model/Catches.cs:15: a catch with no statement and no comment: say why it is handled, log it, or rethrow",
            ],
            EmptyCatches([source])
        );
    }

    private static List<string> EmptyCatches(IReadOnlyList<Source> sources) =>
        [
            .. sources.SelectMany(source =>
                source
                    .Unit.DescendantNodes()
                    .OfType<CatchClauseSyntax>()
                    .Where(c => c.Block.Statements.Count == 0 && !Commented(c.Block))
                    .Select(c =>
                        $"{source.File}:{c.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: a catch with no statement and no comment: say why it is handled, log it, or rethrow"
                    )
            ),
        ];

    private static bool Commented(BlockSyntax block) =>
        block
            .OpenBraceToken.TrailingTrivia.Concat(block.CloseBraceToken.LeadingTrivia)
            .Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia));
}
