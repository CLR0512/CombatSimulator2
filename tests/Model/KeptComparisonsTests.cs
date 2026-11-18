using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class KeptComparisonsTests
{
    private static Comparison Of(int n) => ComparisonsFileTests.Sample($"run-a{n}", $"run-b{n}");

    private static KeptComparisons Six(out Comparison? dropped)
    {
        var kept = new KeptComparisons();
        dropped = null;
        for (var n = 1; n <= 6; n++)
            dropped = kept.Add(Of(n));
        return kept;
    }

    [Fact]
    public void TheNewestIsFirst()
    {
        var kept = new KeptComparisons();
        Assert.Null(kept.Add(Of(1)));
        Assert.Null(kept.Add(Of(2)));
        Assert.Equal(["cmp-run-b2", "cmp-run-b1"], kept.Entries.Select(e => e.Id));
    }

    [Fact]
    public void ASixthDropsTheOldestAndReturnsIt()
    {
        var kept = Six(out var dropped);
        Assert.Equal(KeptComparisons.Max, kept.Entries.Count);
        Assert.Equal("cmp-run-b1", dropped!.Id);
        Assert.Equal(
            ["cmp-run-b6", "cmp-run-b5", "cmp-run-b4", "cmp-run-b3", "cmp-run-b2"],
            kept.Entries.Select(e => e.Id)
        );
    }

    [Fact]
    public void TheDroppedOnesRunsAreNoLongerProtected()
    {
        var kept = Six(out _);
        Assert.DoesNotContain("run-a1", kept.RunIds);
        Assert.DoesNotContain("run-b1", kept.RunIds);
        Assert.False(kept.Uses("run-a1"));
        Assert.Equal(10, kept.RunIds.Count);
        Assert.True(kept.Uses("run-a2"));
        Assert.True(kept.Uses("run-b6"));
    }

    // A swap makes a new record of the same two runs the other way round: it takes its entry's place, first.
    [Fact]
    public void ASwapReplacesItsEntryAndMovesFirst()
    {
        var kept = Six(out _);
        var swapped = ComparisonsFileTests.Sample("run-b3", "run-a3");
        Assert.Null(kept.Add(swapped));
        Assert.Equal(KeptComparisons.Max, kept.Entries.Count);
        Assert.Equal(
            ["cmp-run-a3", "cmp-run-b6", "cmp-run-b5", "cmp-run-b4", "cmp-run-b2"],
            kept.Entries.Select(e => e.Id)
        );
        Assert.Same(swapped, kept.Entries[0]);
    }

    // The same comparison run again and answered from earlier results names the same two runs.
    [Fact]
    public void ARerunOfTheSamePairReplacesItsEntry()
    {
        var kept = new KeptComparisons();
        kept.Add(Of(1));
        kept.Add(Of(2));
        var again = Of(1);
        Assert.Null(kept.Add(again));
        Assert.Equal(["cmp-run-b1", "cmp-run-b2"], kept.Entries.Select(e => e.Id));
        Assert.Same(again, kept.Entries[0]);
    }

    [Fact]
    public void ASharedBaselineIsTwoEntries()
    {
        var kept = new KeptComparisons();
        kept.Add(ComparisonsFileTests.Sample("run-base", "run-b1"));
        kept.Add(ComparisonsFileTests.Sample("run-base", "run-b2"));
        Assert.Equal(2, kept.Entries.Count);
        Assert.Equal(3, kept.RunIds.Count);
    }

    [Fact]
    public void RemoveStopsKeepingOne()
    {
        var kept = Six(out _);
        Assert.True(kept.Remove("cmp-run-b4"));
        Assert.False(kept.Remove("cmp-run-b4"));
        Assert.Equal(4, kept.Entries.Count);
        Assert.False(kept.Uses("run-a4"));
        Assert.Null(kept.Add(Of(7)));
        Assert.Equal(KeptComparisons.Max, kept.Entries.Count);
    }

    [Fact]
    public void AListReadFromDiskKeepsItsOrderAndTheCap()
    {
        var kept = new KeptComparisons([Of(7), Of(6), Of(5), Of(4), Of(3), Of(2), Of(1)]);
        Assert.Equal(
            ["cmp-run-b7", "cmp-run-b6", "cmp-run-b5", "cmp-run-b4", "cmp-run-b3"],
            kept.Entries.Select(e => e.Id)
        );
    }

    [Fact]
    public void AKeptComparisonSaysWhichRunIsGoneAndWhenABuildDiffers()
    {
        var record = Of(1);
        var a = new RunHeader { Id = record.A, GameBuild = "1 a" };
        var b = new RunHeader { Id = record.B, GameBuild = "1 a" };
        Assert.Null(new KeptComparison(record, a, b).GoneRun);
        Assert.Equal(record.A, new KeptComparison(record, null, b).GoneRun);
        Assert.Equal(record.B, new KeptComparison(record, a, null).GoneRun);
        Assert.Equal(record.A, new KeptComparison(record, null, null).GoneRun);
        Assert.False(new KeptComparison(record, a, b).AnotherBuild("1 a"));
        Assert.True(new KeptComparison(record, a, b).AnotherBuild("2 b"));
        Assert.True(
            new KeptComparison(record, a, new RunHeader { Id = record.B, GameBuild = "2 b" }).AnotherBuild("1 a")
        );
        Assert.False(new KeptComparison(record, null, null).AnotherBuild("1 a"));
    }
}
