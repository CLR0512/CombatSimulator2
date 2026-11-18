using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class MasterFiguresTests
{
    private const string SlayerXp = "xp:MelvorBase:Slayer";

    // A monster fought on task: 30 batches of kills, each taking secondsPerKill and giving xpPerKill Slayer XP, with
    // a little noise so ranges have width.
    internal static TargetResult Part(
        string key,
        double secondsPerKill,
        double xpPerKill,
        int killsPerBatch = 10,
        int deathsPerBatch = 0,
        int seed = 1
    )
    {
        var random = new Random(seed);
        var result = new TargetResult
        {
            Key = key,
            Name = key,
            Kind = TargetKind.Enemy,
            State = TargetState.Finished,
            TrialsDone = 30 * (killsPerBatch + deathsPerBatch),
        };
        for (var b = 0; b < 30; b++)
        {
            var tally = new Tally
            {
                Trials = killsPerBatch + deathsPerBatch,
                Kills = killsPerBatch,
                Deaths = deathsPerBatch,
                GameSeconds = killsPerBatch * secondsPerKill * (0.9 + random.NextDouble() * 0.2),
            };
            if (killsPerBatch > 0)
                tally.Xp["MelvorBase:Slayer"] = killsPerBatch * xpPerKill;
            result.Batches.Add(tally);
        }
        return result;
    }

    private static List<TargetResult> Exact(params (string Key, double Seconds, double Xp)[] parts) =>
        [
            .. parts.Select(p =>
            {
                var part = Part(p.Key, p.Seconds, p.Xp);
                foreach (var batch in part.Batches)
                    batch.GameSeconds = batch.Kills * p.Seconds;
                return part;
            }),
        ];

    [Fact]
    public void ARateIsTheSumOfValuePerKillOverTheSumOfTimePerKill()
    {
        var parts = Exact(("A/Fast", 10, 5), ("A/Slow", 30, 20));
        var xp = MasterFigures.Estimate(parts, SlayerXp, TimeUnit.Hour)!;
        Assert.Equal((5 + 20) / 40.0 * 3600, xp.Point, 6);
        // A plain average of the two rates overrates the fast monster's share of the time.
        var plain = (5 / 10.0 + 20 / 30.0) / 2 * 3600;
        Assert.NotEqual(plain, xp.Point, 3);
        var kills = MasterFigures.Estimate(parts, Metrics.Kills, TimeUnit.Hour)!;
        Assert.Equal(2 / 40.0 * 3600, kills.Point, 6);
    }

    [Fact]
    public void SharesAreEachMonstersTimePerKillOverTheSum()
    {
        var shares = MasterFigures.Shares(Exact(("A/Fast", 10, 5), ("A/Slow", 30, 20)));
        Assert.Equal(0.25, shares[0].Share, 6);
        Assert.Equal(0.75, shares[1].Share, 6);
        Assert.Equal(1, shares.Sum(s => s.Share), 9);
    }

    [Fact]
    public void EqualPartsGiveTheirOwnRate()
    {
        var one = Part("A/One", 12, 7, seed: 4);
        var same = MasterFigures.Estimate([one, Part("A/One", 12, 7, seed: 4)], SlayerXp, TimeUnit.Hour)!;
        var alone = Metrics.Estimate(one, SlayerXp, TimeUnit.Hour)!;
        Assert.Equal(alone.Point, same.Point, 6);
    }

    [Fact]
    public void FiguresCarryARangeAroundThePoint()
    {
        var xp = MasterFigures.Estimate(
            [Part("A/Fast", 10, 5, seed: 2), Part("A/Slow", 30, 20, seed: 3)],
            SlayerXp,
            TimeUnit.Hour
        )!;
        Assert.True(xp.Low < xp.Point && xp.Point < xp.High);
        Assert.Equal(30, xp.N);
    }

    [Fact]
    public void APartWithNoKillMakesTheMasterStuck()
    {
        var dies = Part("A/Dragon", 60, 0, killsPerBatch: 0, deathsPerBatch: 3);
        var parts = new List<TargetResult> { Part("A/Fast", 10, 5), dies };
        var stuck = MasterFigures.Stuck(parts)!;
        Assert.Equal("A/Dragon", stuck.Key);
        Assert.Equal("dies every time", stuck.Reason.English());
        Assert.Null(MasterFigures.Estimate(parts, SlayerXp, TimeUnit.Hour));
        Assert.Empty(MasterFigures.Shares(parts));
        Assert.Null(MasterFigures.Risk(parts));
    }

    [Fact]
    public void AnUnfinishedPartIsStuckWithItsReason()
    {
        var refused = new TargetResult
        {
            Key = "A/Locked",
            Name = "Locked",
            State = TargetState.Refused,
            Reasons = [Note.Text("Slayer 99")],
        };
        Assert.Equal("Slayer 99", MasterFigures.Stuck([Part("A/Fast", 10, 5), refused])!.Reason.English());
    }

    [Fact]
    public void AMasterHasNoDeathRatePerTrial() =>
        Assert.Null(MasterFigures.Estimate([Part("A/Fast", 10, 5)], Metrics.DeathRate, TimeUnit.Hour));

    [Fact]
    public void RiskIsDeathsOverTheMastersTimeAndNamesWhereTheyComeFrom()
    {
        var parts = Exact(("A/Fast", 10, 5), ("A/Slow", 30, 20));
        foreach (var batch in parts[1].Batches)
            batch.Deaths = 1;
        var risk = MasterFigures.Risk(parts)!;
        // One death every 10 kills of the slow monster: 0.1 deaths per 40 s of the master's time per kill pair.
        Assert.Equal(0.1 / 40 * 3600, risk.Deaths.Point, 6);
        Assert.Equal("A/Slow", risk.MostFrom);
    }
}
