using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RankingMastersTests
{
    private const string SlayerXp = "xp:MelvorBase:Slayer";

    private static TargetResult Master(string id, double fastXp, TargetResult? stuckPart = null)
    {
        var master = MetricsMasterTests.Master(
            MasterFiguresTests.Part("A/Fast", 10, fastXp, seed: 2),
            stuckPart ?? MasterFiguresTests.Part("A/Slow", 30, 20, seed: 3)
        );
        master.Key = "master:MelvorBase:" + id;
        master.Name = id;
        return master;
    }

    [Fact]
    public void MastersRankByTheirFiguresAndAStuckOneComesAfterThemWithItsReason()
    {
        var dies = MasterFiguresTests.Part("A/Dragon", 60, 0, killsPerBatch: 0, deathsPerBatch: 3);
        dies.Name = "Dragon";
        var masters = new[] { Master("Low", 1), Master("Stuck", 50, dies), Master("High", 50) };
        var run = new Run { Targets = [.. masters.Select(m => m.Key)] };
        foreach (var master in masters)
            run.Results[master.Key] = master;
        var table = Ranking.Rank(run, SlayerXp, TimeUnit.Hour);
        Assert.Equal(["High", "Low", "Stuck"], table.Rows.Select(r => r.Name));
        Assert.Equal(1, table.Rows[0].Rank);
        Assert.Null(table.Rows[2].Rank);
        Assert.Equal("Gets stuck on Dragon: dies every time", table.Rows[2].Reason);
    }
}
