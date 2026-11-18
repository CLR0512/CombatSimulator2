using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class NoneSurvivedTests
{
    private static Tally Batch(int trials, int deaths) =>
        new()
        {
            Trials = trials,
            Kills = trials - deaths,
            Deaths = deaths,
            GameSeconds = trials * 10,
        };

    private static TargetResult Result(TargetState state, params Tally[] batches) =>
        new()
        {
            Key = "MelvorBase:Murkwoods/MelvorBase:MurkwoodSpinner",
            Kind = TargetKind.Enemy,
            State = state,
            TrialsDone = batches.Sum(b => b.Trials),
            Batches = [.. batches],
        };

    [Fact]
    public void EveryTrialDied()
    {
        var result = Result(TargetState.Finished, Batch(500, 500), Batch(500, 500));
        Assert.True(result.NoneSurvived);
    }

    [Fact]
    public void OneSurvivedOfAThousandIsNotEveryTrial()
    {
        // The death rate shown for this is 99.9%, which a coarser rounding would call 100%.
        var result = Result(TargetState.Finished, Batch(500, 500), Batch(500, 499));
        Assert.False(result.NoneSurvived);
    }

    [Fact]
    public void NoDeaths()
    {
        Assert.False(Result(TargetState.Finished, Batch(1000, 0)).NoneSurvived);
    }

    [Fact]
    public void ThreeOfThreeAfterAnEarlyStop()
    {
        var result = Result(TargetState.Finished, Batch(3, 3));
        result.StoppedEarly = true;
        Assert.True(result.NoneSurvived);
    }

    [Theory]
    [InlineData(TargetState.Unfinished)]
    [InlineData(TargetState.Refused)]
    [InlineData(TargetState.Cancelled)]
    [InlineData(TargetState.Fighting)]
    [InlineData(TargetState.Queued)]
    public void OnlyAFinishedResultCanSayIt(TargetState state)
    {
        Assert.False(Result(state, Batch(10, 10)).NoneSurvived);
    }

    [Fact]
    public void AFinishedResultWithoutBatchesDoesNot()
    {
        Assert.False(Result(TargetState.Finished).NoneSurvived);
    }

    [Fact]
    public void ADungeonCountsItsAttempts()
    {
        var died = Result(TargetState.Finished, Batch(100, 100));
        died.Kind = TargetKind.Dungeon;
        died.Stages = 5;
        // Stages were won before each death; the attempt still died.
        died.Fights.Add(new FightRecord { Outcome = FightOutcome.Kill });
        died.Fights.Add(new FightRecord { Stage = 1, Outcome = FightOutcome.Death });
        Assert.True(died.NoneSurvived);

        var cleared = Result(TargetState.Finished, Batch(100, 99));
        cleared.Kind = TargetKind.Dungeon;
        Assert.False(cleared.NoneSurvived);
    }

    [Fact]
    public void AnOlderRunWithoutRiskValuesStillSaysIt()
    {
        var result = Result(TargetState.Finished, Batch(1000, 1000));
        Assert.All(result.Batches, b => Assert.Null(b.MaxHit));
        Assert.False(Risk.Of(result)!.Recorded);
        Assert.True(result.NoneSurvived);
    }

    [Fact]
    public void TrialsThatRanOverDontCountAsSurvived()
    {
        var result = new TargetResult { State = TargetState.Finished };
        result.Batches.Add(
            new Tally
            {
                Trials = 10,
                Deaths = 8,
                RanOver = 2,
            }
        );
        Assert.True(result.NoneSurvived);
        result.Batches.Add(
            new Tally
            {
                Trials = 10,
                Deaths = 9,
                Kills = 1,
                RanOver = 0,
            }
        );
        Assert.False(result.NoneSurvived);
        var allOver = new TargetResult { State = TargetState.Finished };
        allOver.Batches.Add(new Tally { Trials = 3, RanOver = 3 });
        Assert.False(allOver.NoneSurvived);
    }
}
