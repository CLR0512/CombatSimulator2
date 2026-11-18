using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunHistoryTests
{
    [Fact]
    public void NamesAreTrimmedCleanedAndCut()
    {
        Assert.Equal("Before the sword", RunHistory.CleanName("  Before the sword \t"));
        Assert.Equal("ab", RunHistory.CleanName("a\u0007\nb"));
        Assert.Equal(new string('x', 40), RunHistory.CleanName(new string('x', 55)));
        Assert.Null(RunHistory.CleanName(""));
        Assert.Null(RunHistory.CleanName("   "));
        Assert.Null(RunHistory.CleanName("\u0001\u0002"));
        Assert.Null(RunHistory.CleanName(null));
    }

    [Fact]
    public void ANameIsNeverCutInsideACharacter()
    {
        // 39 letters then an emoji (two UTF-16 units): the emoji doesn't fit whole, so it goes.
        var name = RunHistory.CleanName(new string('x', 39) + "😀" + "y");
        Assert.Equal(new string('x', 39), name);
    }

    private static RunHeader Header(string id, bool pinned = false) => new() { Id = id, Pinned = pinned };

    [Fact]
    public void DeletingIsRefusedWithWhy()
    {
        var comparison = new HashSet<string> { "run-a", "run-b" };
        Assert.Equal("the run is in progress", RunHistory.CanDelete(Header("run-x"), "run-x", comparison)?.English());
        Assert.Equal(
            "the run is pinned: unpin it first",
            RunHistory.CanDelete(Header("run-x", pinned: true), null, comparison)?.English()
        );
        Assert.Equal(
            "a kept comparison uses this run",
            RunHistory.CanDelete(Header("run-b"), null, comparison)?.English()
        );
        Assert.Null(RunHistory.CanDelete(Header("run-x"), "run-y", comparison));
    }

    // The refusal holds for a run any kept comparison names, not only the newest one's.
    [Fact]
    public void ARunOfAnyKeptComparisonIsNotDeleted()
    {
        var kept = new KeptComparisons();
        for (var n = 1; n <= 3; n++)
            kept.Add(ComparisonsFileTests.Sample($"run-a{n}", $"run-b{n}"));
        foreach (var id in new[] { "run-a1", "run-b1", "run-a2", "run-b3" })
            Assert.Equal(
                "a kept comparison uses this run",
                RunHistory.CanDelete(Header(id), null, kept.RunIds)?.English()
            );
        Assert.Null(RunHistory.CanDelete(Header("run-x"), null, kept.RunIds));
        kept.Remove("cmp-run-b1");
        Assert.Null(RunHistory.CanDelete(Header("run-a1"), null, kept.RunIds));
        Assert.NotNull(RunHistory.CanDelete(Header("run-a2"), null, kept.RunIds));
    }

    [Fact]
    public void TheBuildWarningIsTheComparisonsOwn()
    {
        var run = ResultsModelTests.SampleRun();
        run.Cs2Mvid = "mvid-1";
        Assert.Null(RunHistory.BuildWarning(run, run.GameBuild, "mvid-1"));

        var current = ResultsModelTests.SampleRun();
        current.GameBuild = "26316999 0.12.0";
        var expected = ComparisonWarnings.Of(run, current, "mvid-2").Select(n => n.English()).ToList();
        Assert.Equal(2, expected.Count);
        Assert.Equal(expected, RunHistory.BuildWarning(run, current.GameBuild, "mvid-2")!.Select(n => n.English()));

        // Only the CS2 build differs.
        var cs2Only = RunHistory.BuildWarning(run, run.GameBuild, "mvid-2")!;
        Assert.Equal("The baseline was simulated by an earlier version of CS2.", Assert.Single(cs2Only).English());
    }
}
