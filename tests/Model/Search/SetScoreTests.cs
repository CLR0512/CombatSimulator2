using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SetScoreTests
{
    private const string Xp = "xp:MelvorBase:Slayer";

    // A target fought with a set: a kill every `seconds`, `xp` a kill, a little noise from its seed.
    private static TargetResult Target(string key, double seconds, double xp, int seed) =>
        MasterFiguresTests.Part(key, seconds, xp, seed: seed);

    private static TargetResult Set(List<double>? weights, params TargetResult[] parts) =>
        new()
        {
            Key = "area:MelvorBase:Farm",
            Kind = TargetKind.Set,
            State = TargetState.Finished,
            Parts = [.. parts],
            Weights = weights,
        };

    private static List<double> WeightsOf(TargetResult current) =>
        [.. current.Parts!.Select(p => 1 / Metrics.Estimate(p, Xp, TimeUnit.Hour)!.Point)];

    [Fact]
    public void EachTargetCountsTheSameWhateverItsRate()
    {
        // A fast target worth a lot and a slow one worth little: the set gains 10% on the slow one only.
        var current = Set(null, Target("Farm/Fast", 1, 100, 1), Target("Farm/Slow", 100, 1, 2));
        var weights = WeightsOf(current);
        current.Weights = weights;
        var better = Set(weights, Target("Farm/Fast", 1, 100, 1), Target("Farm/Slow", 100, 1.1, 2));
        var a = Metrics.Estimate(current, Xp, TimeUnit.Hour)!;
        var b = Metrics.Estimate(better, Xp, TimeUnit.Hour)!;
        Assert.Equal(1, a.Point, 9);
        Assert.Equal(1.05, b.Point, 9);
        var gains = SetScore.PerMember(current, better, Xp);
        Assert.Equal(0, gains[0].Gain!.Point, 9);
        Assert.Equal(0.1, gains[1].Gain!.Point, 9);
        Assert.Equal(1, SetScore.Helps(gains, lowerIsBetter: false));
    }

    [Fact]
    public void ASetHasNoFigureBeforeItsWeights() =>
        Assert.Null(Metrics.Estimate(Set(null, Target("Farm/Fast", 1, 100, 1)), Xp, TimeUnit.Hour));

    [Fact]
    public void ATargetCurrentGearGetsNothingFromCountsForNothing()
    {
        var set = Set([0, 1 / 36.0], Target("Farm/Never", 10, 5, 1), Target("Farm/Some", 100, 1, 2));
        Assert.InRange(Metrics.Estimate(set, Xp, TimeUnit.Hour)!.Point, 0.95, 1.05);
    }

    [Fact]
    public void ASetsDeathsAreItsTargets()
    {
        var dies = MasterFiguresTests.Part("Farm/Dies", 10, 5, deathsPerBatch: 1);
        var set = Set(null, Target("Farm/Fast", 1, 100, 1), dies);
        Assert.Equal((30, 300 + 330), DeathLimits.Counts(set));
    }

    [Fact]
    public void ATargetAnotherSetDoesClearlyBetterOnWantsItsOwnSet()
    {
        var best = Set([1, 1], Target("Farm/Fast", 10, 5, 1), Target("Farm/Slow", 100, 5, 2));
        var rival = Set([1, 1], Target("Farm/Fast", 10, 4, 3), Target("Farm/Slow", 100, 10, 4));
        var close = Set([1, 1], Target("Farm/Fast", 10, 5, 5), Target("Farm/Slow", 100, 5, 6));
        Assert.Equal(["Farm/Slow"], SetScore.WantsOwnSet(best, [rival, close], Xp, lowerIsBetter: false));
        Assert.Empty(SetScore.WantsOwnSet(best, [close], Xp, lowerIsBetter: false));
    }
}
