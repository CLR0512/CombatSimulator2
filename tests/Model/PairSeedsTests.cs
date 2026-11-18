using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PairSeedsTests
{
    [Fact]
    public void EachTargetGetsTheSeedItsResultCameFrom()
    {
        var fresh = RankingTests.Finished("t:fresh", "Fresh", 1);
        var reused = RankingTests.Finished("t:reused", "Reused", 1);
        reused.ReusedFrom = new ReuseOrigin { RunId = "run-old", Seed = 11 };
        var paired = RankingTests.Finished("t:paired", "Paired", 1);
        paired.RunSeed = 22;
        var run = RankingTests.RunOf([fresh, reused, paired], "t:queued");
        run.Seed = 5;

        var seeds = PairSeeds.Of(run);

        Assert.Equal(5UL, seeds["t:fresh"]);
        Assert.Equal(11UL, seeds["t:reused"]);
        Assert.Equal(22UL, seeds["t:paired"]);
        Assert.Equal(5UL, seeds["t:queued"]);
    }
}
