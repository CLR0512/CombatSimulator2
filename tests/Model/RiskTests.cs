using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RiskTests
{
    private static Tally Batch(int trials, int deaths, long? maxHit, long? minHp, int? closeCalls) =>
        new()
        {
            Trials = trials,
            Kills = trials - deaths,
            Deaths = deaths,
            GameSeconds = trials * 10,
            MaxHit = maxHit,
            MinHp = minHp,
            CloseCalls = closeCalls,
        };

    private static TargetResult Result(params Tally[] batches) =>
        new()
        {
            Key = "MelvorBase:GolbinForest/MelvorBase:Golbin",
            Kind = TargetKind.Enemy,
            State = TargetState.Finished,
            TrialsDone = batches.Sum(b => b.Trials),
            Batches = [.. batches],
        };

    [Fact]
    public void AddTakesTheMaxTheMinAndTheSum()
    {
        var a = Batch(1, 0, 30, 50, 0);
        a.Add(Batch(1, 0, 40, 70, 1));
        Assert.Equal((40L, 50L, 1), (a.MaxHit, a.MinHp, a.CloseCalls));
        a.Add(Batch(1, 0, 10, 20, 1));
        Assert.Equal((40L, 20L, 2), (a.MaxHit, a.MinHp, a.CloseCalls));
    }

    [Fact]
    public void AddWithANullSideKeepsTheOther()
    {
        var a = new Tally();
        a.Add(Batch(1, 1, 90, null, 0)); // a death: no lowest HP
        Assert.Equal((90L, (long?)null, 0), (a.MaxHit, a.MinHp, a.CloseCalls));
        a.Add(Batch(1, 0, 20, 35, 0));
        Assert.Equal((90L, 35L, 0), (a.MaxHit, a.MinHp, a.CloseCalls));
        var b = Batch(1, 0, 20, 35, 1);
        b.Add(new Tally { Trials = 1, Kills = 1 }); // nothing recorded on that side
        Assert.Equal((20L, 35L, 1), (b.MaxHit, b.MinHp, b.CloseCalls));
        var none = new Tally();
        none.Add(new Tally());
        Assert.Null(none.MaxHit);
        Assert.Null(none.MinHp);
        Assert.Null(none.CloseCalls);
    }

    [Fact]
    public void ClearForgetsTheRiskValues()
    {
        var a = Batch(1, 0, 30, 50, 1);
        a.Clear();
        Assert.Null(a.MaxHit);
        Assert.Null(a.MinHp);
        Assert.Null(a.CloseCalls);
    }

    [Fact]
    public void RiskIsTakenOverTheBatches()
    {
        var risk = Risk.Of(Result(Batch(10, 0, 40, 60, 0), Batch(10, 1, 75, 52, 2), Batch(10, 0, 30, 80, 1)))!;
        Assert.True(risk.Recorded);
        Assert.Equal(75, risk.HighestHit);
        Assert.Equal(52, risk.LowestHp);
        var expected = Estimators.Wilson(3, 30);
        Assert.Equal(expected, risk.CloseCalls);
        Assert.True(risk.Warning);
    }

    [Fact]
    public void DeathsLeaveTheLowestHpAlone()
    {
        // The second batch only died, so only the first sets the lowest HP.
        var risk = Risk.Of(Result(Batch(5, 0, 20, 90, 0), Batch(5, 5, 400, null, 0)))!;
        Assert.Equal(400, risk.HighestHit);
        Assert.Equal(90, risk.LowestHp);
        Assert.True(risk.Warning);
    }

    [Fact]
    public void EveryTrialDiedLeavesNoLowestHpAndNoWarning()
    {
        var risk = Risk.Of(Result(Batch(3, 3, 120, null, 0), Batch(3, 3, 140, null, 0)))!;
        Assert.True(risk.Recorded);
        Assert.Equal(140, risk.HighestHit);
        Assert.Null(risk.LowestHp);
        Assert.False(risk.Warning);
        Assert.Equal(0, risk.CloseCalls!.Point);
    }

    [Fact]
    public void TheWarningShowsWhenTheHighestHitReachesTheLowestHp()
    {
        Assert.True(Risk.Of(Result(Batch(4, 0, 50, 50, 1)))!.Warning);
        Assert.False(Risk.Of(Result(Batch(4, 0, 49, 50, 0)))!.Warning);
    }

    [Fact]
    public void AnOlderResultHasNoRisk()
    {
        var risk = Risk.Of(Result(Batch(4, 0, null, null, null), Batch(4, 1, null, null, null)))!;
        Assert.False(risk.Recorded);
        Assert.False(risk.Warning);
        Assert.Null(risk.LowestHp);
        Assert.Null(risk.CloseCalls);
    }

    [Fact]
    public void ATargetThatNeverHitsGivesNoWarning()
    {
        // Survived trials with no hit: highest hit 0, lowest HP the HP each ended with.
        var risk = Risk.Of(Result(Batch(10, 0, 0, 600, 0)))!;
        Assert.Equal(0, risk.HighestHit);
        Assert.Equal(600, risk.LowestHp);
        Assert.False(risk.Warning);
    }

    [Fact]
    public void OnlyFinishedResultsHaveRisk()
    {
        var result = Result(Batch(4, 0, 50, 50, 1));
        result.State = TargetState.Unfinished;
        Assert.Null(Risk.Of(result));
        Assert.Null(Risk.Of(new TargetResult { State = TargetState.Finished }));
    }
}
