using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class CompareTableTests
{
    private const string Strength = "xp:MelvorBase:Strength";

    // A: every target at 100 XP; B: better on t:up, worse on t:down, equal on t:same, refused on t:no.
    private static (Run A, Run B) Sides()
    {
        var a = RankingTests.RunOf([
            RankingTests.Finished("t:same", "Same", 100),
            RankingTests.Finished("t:down", "Down", 100),
            RankingTests.Finished("t:up", "Up", 100),
            RankingTests.Finished("t:no", "No", 100),
            RankingTests.Finished("t:big", "Big", 100),
        ]);
        var b = RankingTests.RunOf([
            RankingTests.Finished("t:same", "Same", 100),
            RankingTests.Finished("t:down", "Down", 60),
            RankingTests.Finished("t:up", "Up", 130),
            RankingTests.Other("t:no", "No", TargetState.Refused, "locked"),
            RankingTests.Finished("t:big", "Big", 200),
        ]);
        return (a, b);
    }

    [Fact]
    public void ByDifferenceBetterFirstUnrankedLast()
    {
        var (a, b) = Sides();
        var table = CompareTables.Of(a, b, Strength, TimeUnit.Hour);
        Assert.Equal(["t:big", "t:up", "t:same", "t:down", "t:no"], table.Rows.Select(r => r.Key));
        Assert.Equal([1, 2, 3, 4], table.Rows.Take(4).Select(r => r.Rank!.Value));
        Assert.Null(table.Rows[^1].Rank);
        Assert.Equal((2, 1, 1, 1), (table.Better, table.Worse, table.NoClearDifference, table.NoDifference));
    }

    [Fact]
    public void LowerIsBetterFlipsTheOrder()
    {
        var a = RankingTests.RunOf([
            RankingTests.Finished("t:slow", "Slow", 1, seconds: 60),
            RankingTests.Finished("t:fast", "Fast", 1, seconds: 60),
        ]);
        var b = RankingTests.RunOf([
            RankingTests.Finished("t:slow", "Slow", 1, seconds: 90),
            RankingTests.Finished("t:fast", "Fast", 1, seconds: 30),
        ]);
        var table = CompareTables.Of(a, b, Metrics.KillTime, TimeUnit.Hour);
        Assert.True(table.LowerIsBetter);
        Assert.Equal(["t:fast", "t:slow"], table.Rows.Select(r => r.Key));
        Assert.Equal(Verdict.Better, table.Rows[0].Difference.Verdict);
    }

    [Fact]
    public void BySideRanksThatSidesValue()
    {
        var (a, b) = Sides();
        var byB = CompareTables.Of(a, b, Strength, TimeUnit.Hour, order: CompareOrder.B);
        Assert.Equal(["t:big", "t:up", "t:same", "t:down", "t:no"], byB.Rows.Select(r => r.Key));
        var byA = CompareTables.Of(a, b, Strength, TimeUnit.Hour, order: CompareOrder.A);
        Assert.Equal(5, byA.Rows.Count(r => r.Rank != null)); // A has every target
    }

    [Theory]
    [InlineData(CompareFilter.Better, new[] { "t:big", "t:up" })]
    [InlineData(CompareFilter.Worse, new[] { "t:down" })]
    public void FiltersKeepOneVerdictButCountAll(CompareFilter filter, string[] keys)
    {
        var (a, b) = Sides();
        var table = CompareTables.Of(a, b, Strength, TimeUnit.Hour, filter: filter);
        Assert.Equal(keys, table.Rows.Select(r => r.Key));
        Assert.Equal(2, table.Better);
    }

    [Fact]
    public void HoldKeepsShownRowsInPlace()
    {
        var (a, b) = Sides();
        var table = CompareTables.Of(a, b, Strength, TimeUnit.Hour);
        var held = table.Hold(["t:down", "t:same"]);
        Assert.Equal(["t:down", "t:same", "t:big", "t:up", "t:no"], held.Rows.Select(r => r.Key));
        Assert.Same(table, table.Hold(["t:down"], final: true));
    }

    // Sides() with B dying every trial on its two best targets, and A on the one B is worse at.
    private static (Run A, Run B) SidesWithDeaths()
    {
        var (a, b) = Sides();
        b.Results["t:big"] = RankingTests.Died("t:big", "Big", 200);
        b.Results["t:up"] = RankingTests.Died("t:up", "Up", 130);
        a.Results["t:down"] = RankingTests.Died("t:down", "Down", 100);
        return (a, b);
    }

    [Fact]
    public void ADiedSideCountsWithTheRowsWithoutAVerdict()
    {
        var (a, b) = SidesWithDeaths();
        var table = CompareTables.Of(a, b, Strength, TimeUnit.Hour);
        // t:big, t:up and t:down died on a side; t:no is refused on B; t:same is the one judged.
        Assert.Equal((0, 0, 1, 4), (table.Better, table.Worse, table.NoClearDifference, table.NoDifference));
        var reasons = table.Rows.ToDictionary(r => r.Key, r => r.Difference.Reason?.English());
        Assert.Equal("B died every fight", reasons["t:big"]);
        Assert.Equal("B died every fight", reasons["t:up"]);
        Assert.Equal("A died every fight", reasons["t:down"]);
        Assert.Null(reasons["t:same"]);
        Assert.Null(reasons["t:no"]);
    }

    [Theory]
    [InlineData(CompareFilter.Better)]
    [InlineData(CompareFilter.Worse)]
    public void TheBetterAndWorseFiltersLeaveDiedRowsOut(CompareFilter filter)
    {
        var (a, b) = SidesWithDeaths();
        var table = CompareTables.Of(a, b, Strength, TimeUnit.Hour, filter: filter);
        Assert.Empty(table.Rows);
        Assert.Equal(4, table.NoDifference);
    }

    [Fact]
    public void BothSidesDiedIsOneRowWithoutAVerdict()
    {
        var a = RankingTests.RunOf([RankingTests.Died("t:1", "One", 100)]);
        var b = RankingTests.RunOf([RankingTests.Died("t:1", "One", 150)]);
        var table = CompareTables.Of(a, b, Strength, TimeUnit.Hour);
        Assert.Equal((0, 0, 0, 1), (table.Better, table.Worse, table.NoClearDifference, table.NoDifference));
        Assert.Equal("Both died every fight", table.Rows.Single().Difference.Reason!.English());
    }
}
