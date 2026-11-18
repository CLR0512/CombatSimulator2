using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SearchProgressTests
{
    private static readonly int[] Stages = [100, 400, 1000];

    [Fact]
    public void ARoundGrowsAsItsSetsAreFought()
    {
        Assert.Equal(0, SearchProgress.Round(Stages, 1, 0, 40, 0));
        var half = SearchProgress.Round(Stages, 1, 0, 40, 20);
        var all = SearchProgress.Round(Stages, 1, 0, 40, 40);
        Assert.InRange(half, 0.01, all);
        // The last stage's last set ends the round.
        Assert.Equal(1, SearchProgress.Round(Stages, 3, 100 * 40 + 400 * 10, 3, 3), 6);
    }

    [Fact]
    public void ARoundBeforeItStartsIsNothing() => Assert.Equal(0, SearchProgress.Round(Stages, 0, 0, 10, 0));

    [Fact]
    public void AClimbsStepsShareItsPasses()
    {
        // Twelve steps a pass, three passes at most.
        Assert.Equal(0, SearchProgress.Climb(1, 0, 12, 0, 3));
        Assert.Equal(1.0 / 36, SearchProgress.Climb(1, 0, 12, 1, 3), 6);
        Assert.Equal(0.5, SearchProgress.Climb(2, 6, 12, 0, 3), 6);
        Assert.Equal(1, SearchProgress.Climb(3, 11, 12, 1, 3), 6);
        // A step past what the pass was thought to hold counts as its last.
        Assert.Equal(1, SearchProgress.Climb(3, 14, 12, 1, 3), 6);
    }

    [Fact]
    public void AClimbSharesWhatIsLeftWithThoseQueuedAndKeepsSomeForARestart()
    {
        Assert.Equal(0.8, SearchProgress.Share(0, 0), 6);
        Assert.Equal(0.4, SearchProgress.Share(0, 1), 6);
        Assert.Equal(0.4, SearchProgress.Share(0.5, 0), 6);
    }

    [Fact]
    public void TheBestSoFarIsTheLeaderOverCurrentGear()
    {
        var current = new StageResult(0, RankingTests.Finished("t", "t", 10), "xp:MelvorBase:Strength");
        var better = new StageResult(4, RankingTests.Finished("t", "t", 20), "xp:MelvorBase:Strength");
        var best = SearchProgress.BestSoFar([current, better], new Goal(Metrics.MainXp), 0.01);
        Assert.Equal(4, best!.Value.Index);
        Assert.True(best.Value.Percent!.Point > 0.9);
        Assert.Null(SearchProgress.BestSoFar([current], new Goal(Metrics.MainXp), 0.01));
    }

    [Fact]
    public void TimeLeftFollowsThePaceSoFar() =>
        // A quarter done in 30 s: three quarters left at the same pace.
        Assert.Equal(90, SearchProgress.SecondsLeft(30, 0.25, null)!.Value, 6);

    [Fact]
    public void AClimbHasNoMoreLeftThanItsLimit()
    {
        Assert.Equal(40, SearchProgress.SecondsLeft(30, 0.25, 40)!.Value, 6);
        // Past the limit, the climb ends at its step under way: nothing left to count.
        Assert.Equal(0, SearchProgress.SecondsLeft(160, 0.9, -10)!.Value, 6);
    }

    [Fact]
    public void TooEarlyToSayIsNothing()
    {
        Assert.Null(SearchProgress.SecondsLeft(SearchProgress.EstimateAfter - 1, 0.5, null));
        Assert.Null(SearchProgress.SecondsLeft(60, 0.01, null));
    }

    [Fact]
    public void AClimbWithNoPaceYetHasItsTimeToTheLimit() =>
        Assert.Equal(142, SearchProgress.SecondsLeft(8, 0.001, 142)!.Value, 6);

    [Fact]
    public void TheBarFollowsTheTimeLeftAndNeverRunsBack()
    {
        var progress = new SearchProgress();
        Assert.Equal(0.9, progress.Follow(90, 10), 6);
        Assert.Equal(0.9, progress.Follow(95, 100), 6);
        Assert.Equal(0.9, progress.Follow(96, null), 6);
    }

    [Fact]
    public void AClimbNearItsLimitStillHasItsStepUnderWayLeft()
    {
        var progress = new SearchProgress();
        // Half of the step under way done in 20 s, 1 s short of a 120 s limit, nothing queued.
        progress.Update([400, 1000], 1, 0, 0, null);
        Assert.Equal(20, SearchProgress.RoundLeft(20, 0.5)!.Value, 6);
        Assert.Null(SearchProgress.RoundLeft(5, 0.01));
    }

    [Fact]
    public void QueuedClimbsAddTheirLimitToTheTimeLeft()
    {
        var progress = new SearchProgress();
        var alone = progress.Left(progress.Update([400], 1, 10, 9, null), 100, 119, 0, 5, 120);
        var another = new SearchProgress();
        var withOne = another.Left(another.Update([400], 1, 10, 9, null), 100, 119, 1, 5, 120);
        Assert.NotNull(alone);
        Assert.True(withOne > alone);
        Assert.True(withOne <= 121);
    }
}
