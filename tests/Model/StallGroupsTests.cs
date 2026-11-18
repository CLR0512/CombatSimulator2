using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class StallGroupsTests
{
    private const string Strength = "xp:MelvorBase:Strength";

    internal static TargetResult Stalled(
        string key,
        long attacks,
        long hits,
        long hpLeft,
        long healed = 0,
        long armourCut = 0
    )
    {
        var result = RankingTests.Other(key, key, TargetState.Unfinished, "stalled");
        result.Stall = new Stall
        {
            Trials = 1,
            Counts = new FightCounts
            {
                Dealt = new SideCounts
                {
                    Attacks = attacks,
                    Hits = hits,
                    Attack = hits * 10,
                    Healed = healed,
                    ArmourCut = armourCut,
                },
            },
            HpLeft = hpLeft,
            HpMax = 100,
            LimitSeconds = 300,
        };
        return result;
    }

    private static RankTable Rank(params TargetResult[] results) =>
        Ranking.Rank(RankingTests.RunOf(results), Strength, TimeUnit.Hour);

    [Fact]
    public void StalledRowsFollowTheDiedOnesByReasonThenHealthLeftThenRunOrder()
    {
        var table = Rank(
            RankingTests.Other("refused", "refused", TargetState.Refused, "locked"),
            Stalled("slow-b", 10, 5, 40),
            Stalled("armour", 10, 5, 90, armourCut: 1000),
            RankingTests.Died("died", "died", 5),
            Stalled("slow-a", 10, 5, 20),
            Stalled("slow-c", 10, 5, 40),
            Stalled("immune", 10, 0, 100),
            RankingTests.Finished("ranked", "ranked", 10)
        );
        Assert.Equal(
            ["ranked", "died", "immune", "armour", "slow-a", "slow-b", "slow-c", "refused"],
            table.Rows.Select(r => r.Key)
        );
        Assert.Equal(
            [
                new ReasonGroup(StallReason.NeverHit, 1),
                new ReasonGroup(StallReason.Armour, 1),
                new ReasonGroup(StallReason.TooSlow, 3),
            ],
            table.ReasonGroups
        );
        Assert.Equal(StallReason.TooSlow, table.Rows[4].Stall!.Reason);
        Assert.Equal(0.2, table.Rows[4].Stall!.HpLeftShare, 10);
        Assert.Null(table.Rows[4].Rank);
    }

    [Fact]
    public void AGroupOfOneHasItsHeadingAndNoStallNone()
    {
        Assert.Equal(
            [new ReasonGroup(StallReason.HealsBack, 1)],
            Rank(Stalled("wolf", 10, 5, 100, healed: 50)).ReasonGroups
        );
        Assert.Empty(Rank(RankingTests.Finished("a", "a", 10)).ReasonGroups);
    }

    [Fact]
    public void ANarrowedTableCountsOnlyItsRows()
    {
        var table = Rank(Stalled("a", 10, 5, 10), Stalled("b", 10, 5, 20), Stalled("c", 10, 0, 50));
        var narrowed = table.Narrow(new HashSet<string>(["a", "c"]));
        Assert.Equal(
            [new ReasonGroup(StallReason.NeverHit, 1), new ReasonGroup(StallReason.TooSlow, 1)],
            narrowed.ReasonGroups
        );
    }

    [Fact]
    public void AnOldUnfinishedRowStaysWithTheRest()
    {
        var table = Rank(
            RankingTests.Other("old", "old", TargetState.Unfinished, "a trial ran over the 300 s limit"),
            Stalled("new", 10, 5, 10)
        );
        Assert.Equal(["new", "old"], table.Rows.Select(r => r.Key));
        Assert.Null(table.Rows[1].Stall);
        Assert.Single(table.ReasonGroups);
    }

    [Fact]
    public void ARankedRowCarriesItsRanOverShareAndBestIgnoresIt()
    {
        var better = RankingTests.Finished("better", "better", 20);
        foreach (var b in better.Batches)
            b.RanOver = 0;
        better.Batches[0].RanOver = 3;
        var table = Rank(RankingTests.Finished("worse", "worse", 10), better);
        Assert.Equal("better", table.BestKey);
        Assert.Equal(3.0 / 300, table.Rows[0].RanOver!.Value, 10);
        Assert.Null(table.Rows[1].RanOver);
        Assert.Empty(table.ReasonGroups);
    }
}
