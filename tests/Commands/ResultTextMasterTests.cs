using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ResultTextMasterTests
{
    private const string Key = "master:MelvorBase:Kuren";

    private static Run RunOf(TargetResult master)
    {
        var run = new Run
        {
            Id = "run-20261114-120000-1",
            Setup = new Setup { Character = "Chk" },
            Seed = 1,
            MainSkill = "MelvorBase:Slayer",
            Targets = [Key],
            State = RunState.Finished,
        };
        run.Results[Key] = master;
        return run;
    }

    private static TargetResult Kuren(TargetResult? second = null)
    {
        var master = MetricsMasterTests.Master(
            MasterFiguresTests.Part(Ids.PartKey("Kuren", "MelvorBase:Farm/MelvorBase:Cow"), 10, 5),
            second ?? MasterFiguresTests.Part(Ids.PartKey("Kuren", "MelvorBase:Farm/MelvorBase:Jack"), 30, 20)
        );
        master.Name = "Kuren";
        master.Blocked = ["MelvorBase:Forest/MelvorBase:Golbin"];
        return master;
    }

    [Fact]
    public void TheSummaryGivesAMastersKillsAndDeathsOverItsTime()
    {
        var text = ResultText.Summary(RunOf(Kuren()), TimeUnit.Hour, PriceTable.Empty);
        Assert.Contains("master:Kuren finished kills/hour 180", text, StringComparison.Ordinal);
        Assert.Contains("monsters 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetailsListThePoolWithEachShareAndTheBlockedMonsters()
    {
        var run = RunOf(Kuren());
        var text = ResultText.Details(run, run.Results[Key], TimeUnit.Hour, PriceTable.Empty, id => id, false);
        Assert.Contains("Kuren's tasks · 2 monsters, 1 blocked", text, StringComparison.Ordinal);
        Assert.Contains("Farm/Cow  25", text, StringComparison.Ordinal);
        Assert.Contains("Farm/Jack  75", text, StringComparison.Ordinal);
        Assert.Contains("Forest/Golbin  blocked", text, StringComparison.Ordinal);
        Assert.Contains("\nxp:MelvorBase:Slayer 2,", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMonsterOnALoadoutOfItsOwnNamesItOnItsPoolLine()
    {
        var master = Kuren();
        master.Loadouts = new() { ["MelvorBase:Farm/MelvorBase:Jack"] = "MelvorBase:Loadout2" };
        var run = RunOf(master);
        var text = ResultText.Details(
            run,
            master,
            TimeUnit.Hour,
            PriceTable.Empty,
            id => id == "MelvorBase:Loadout2" ? "Loadout 2" : id,
            false
        );
        Assert.Contains("deaths/hour · loadout):", text, StringComparison.Ordinal);
        var lines = text.Split('\n');
        Assert.EndsWith(
            "  Loadout2",
            lines.Single(l => l.Contains("Farm/Jack", StringComparison.Ordinal)),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "Loadout",
            lines.Single(l => l.Contains("Farm/Cow", StringComparison.Ordinal)),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WithoutLoadoutsThePoolHeaderIsAsBefore()
    {
        var run = RunOf(Kuren());
        var text = ResultText.Details(run, run.Results[Key], TimeUnit.Hour, PriceTable.Empty, id => id, false);
        Assert.Contains("deaths/hour):", text, StringComparison.Ordinal);
        Assert.DoesNotContain("loadout", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStuckMasterNamesTheMonsterInPlaceOfItsFigures()
    {
        var dies = MasterFiguresTests.Part(
            Ids.PartKey("Kuren", "MelvorBase:Farm/MelvorBase:Dragon"),
            60,
            0,
            killsPerBatch: 0,
            deathsPerBatch: 3
        );
        var text = ResultText.Summary(RunOf(Kuren(dies)), TimeUnit.Hour, PriceTable.Empty);
        Assert.Contains("master:Kuren finished stuck on Farm/Dragon: dies every time", text, StringComparison.Ordinal);
    }
}
