using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ChecksTests
{
    private static readonly Goal Kills = new(Metrics.Kills);
    private const double OnePercent = 0.01;

    private static CheckRule Rule(params TargetResult[] held) =>
        new([100, 400], held, Metrics.Kills, Kills, OnePercent);

    [Fact]
    public void ChecksAreTheStagesBelowTheLast()
    {
        Assert.Equal([100, 400], Checks.Ladder(1000, 50));
        Assert.Equal([25, 100, 400], Checks.Ladder(1000, 120));
        Assert.Equal([100], Checks.Ladder(400, 10));
        Assert.Empty(Checks.Ladder(100, 10));
        foreach (var (trials, sets) in new[] { (1000, 50), (1000, 500), (400, 10), (5000, 3) })
        {
            var ladder = Checks.Ladder(trials, sets);
            Assert.True(ladder.Zip(ladder.Skip(1)).All(p => p.First < p.Second));
            Assert.All(ladder, c => Assert.True(c < trials));
        }
    }

    [Fact]
    public void ASetIsDroppedAtACheckAsTheRaceDropsItAgainstTheHeldSet()
    {
        var held = Set(0, 12);
        var rule = Rule(held.Result, held.Result);
        foreach (var other in new[] { Set(1, 20), Set(1, 10), Set(1, 12.05), Set(1, 12, deaths: 5) })
        {
            var raced = Race.After([held, other], Kills, OnePercent).Dropped;
            var expected = raced.TryGetValue(1, out var why) ? why : (DropReason?)null;
            Assert.Equal(expected, Checks.At(rule, 0, other.Result, Metrics.Kills));
        }
        Assert.Equal(DropReason.Behind, Checks.At(rule, 1, Set(1, 20).Result, Metrics.Kills));
        Assert.Null(Checks.At(rule, 1, Set(1, 10).Result, Metrics.Kills));
    }

    [Fact]
    public void ASetIsNeverStoppedWhereTheHeldSetHasNoResult()
    {
        // The held set's own fight ended before the second check: nothing is compared there.
        var rule = Rule(Set(0, 12).Result);
        Assert.Equal(DropReason.Behind, Checks.At(rule, 0, Set(1, 20).Result, Metrics.Kills));
        Assert.Null(Checks.At(rule, 1, Set(1, 20).Result, Metrics.Kills));
        // Read only, as the held set is fought: never stopped.
        Assert.Null(Checks.At(Rule(), 0, Set(1, 20).Result, Metrics.Kills));
    }

    [Fact]
    public void ACandidateIsJudgedAsDroptimizerJudgesIt()
    {
        var current = Set(0, 12);
        var rule = Rule(current.Result) with { Candidates = true };
        foreach (var other in new[] { Set(1, 20), Set(1, 10), Set(1, 12) })
        {
            var dropped = Droptimizer.After([current, other], Kills, OnePercent).Dropped;
            var expected = dropped.TryGetValue(1, out var why) ? why : (DropReason?)null;
            Assert.Equal(expected, Checks.At(rule, 0, other.Result, Metrics.Kills));
        }
    }

    [Fact]
    public void WhichSetsAreDroppedDoesNotDependOnTheOrderTheyFinish()
    {
        var held = Set(0, 12);
        var rule = Rule(held.Result, held.Result);
        StageResult[] sets = [Set(1, 20), Set(2, 10), Set(3, 12.05), Set(4, 30, Own), Set(5, 11, deaths: 4)];
        var inOrder = sets.Where(s => Checks.At(rule, 0, s.Result, Metrics.Kills) != null).Select(s => s.Index);
        var reversed = sets.Reverse()
            .Where(s => Checks.At(rule, 0, s.Result, Metrics.Kills) != null)
            .Select(s => s.Index)
            .Order();
        Assert.Equal(inOrder.Order(), reversed);
        Assert.NotEmpty(inOrder);
    }

    [Fact]
    public void ASetOfCurrentGearsStyleIsMeasuredInCurrentGearsSkill()
    {
        var rule = new CheckRule(
            [100],
            [],
            "",
            new Goal(Metrics.MainXp),
            OnePercent,
            CurrentSkill: "MelvorBase:Ranged",
            CurrentStyle: "Ranged"
        );
        Assert.Equal("xp:MelvorBase:Ranged", Checks.MetricOf(rule, "MelvorBase:Defence", "Ranged"));
        Assert.Equal("xp:MelvorBase:Magic", Checks.MetricOf(rule, "MelvorBase:Magic", "Magic"));
    }

    [Fact]
    public void OnlyGoalsReadThroughPricesCarryThem()
    {
        Assert.True(Checks.NeedsPrices(new Goal("value:MelvorBase:GP")));
        Assert.True(Checks.NeedsPrices(new Goal("worth:MelvorBase:GP")));
        Assert.False(Checks.NeedsPrices(new Goal(Metrics.MainXp)));
        Assert.False(Checks.NeedsPrices(Kills));
    }
}
