using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ResultTextTests
{
    private const string Strength = "xp:MelvorBase:Strength";

    private static string[] Lines(Run run, string metric = Strength) =>
        ResultText.Summary(run, TimeUnit.Hour, PriceTable.Empty, metric).Split('\n');

    [Fact]
    public void TheBestTargetsLineEndsWithBest()
    {
        var run = RankingTests.RunOf([
            RankingTests.Finished("area/ant", "Ant", 10),
            RankingTests.Finished("area/bat", "Bat", 30),
            RankingTests.Other("area/x", "X", TargetState.Refused, "locked"),
        ]);
        var lines = Lines(run);
        // The run's order is kept; only the mark is new.
        Assert.StartsWith("area/ant finished", lines[2]);
        Assert.False(lines[2].EndsWith(" best", StringComparison.Ordinal));
        Assert.StartsWith("area/bat finished", lines[3]);
        Assert.EndsWith(" best", lines[3]);
        Assert.Equal("area/x refused locked", lines[4]);
        Assert.DoesNotContain("every fight died:", lines);
        Assert.DoesNotContain("no target survived", lines);
    }

    [Fact]
    public void TargetsWhereEveryFightDiedComeLastUnderTheirOwnLine()
    {
        var run = RankingTests.RunOf([
            RankingTests.Died("area/spinner", "Spinner", 90),
            RankingTests.Finished("area/vines", "Vines", 30),
            RankingTests.Died("area/spider", "Spider", 60),
        ]);
        var lines = Lines(run);
        Assert.StartsWith("area/vines finished", lines[2]);
        Assert.EndsWith(" best", lines[2]);
        Assert.Equal("every fight died:", lines[3]);
        Assert.StartsWith("area/spinner finished kills/hour", lines[4]);
        Assert.EndsWith(" died", lines[4]);
        Assert.StartsWith("area/spider finished", lines[5]);
        Assert.EndsWith(" died", lines[5]);
        Assert.Equal(6, lines.Length);
        Assert.DoesNotContain(lines, l => l.EndsWith(" best", StringComparison.Ordinal) && l != lines[2]);
    }

    [Fact]
    public void WhenNothingSurvivedItSaysSo()
    {
        var run = RankingTests.RunOf([
            RankingTests.Died("area/a", "A", 10),
            RankingTests.Other("area/x", "X", TargetState.Refused, "locked"),
        ]);
        var lines = Lines(run);
        Assert.Equal(["area/x refused locked", "no target survived", "every fight died:"], lines.Skip(2).Take(3));
        Assert.EndsWith(" died", lines[5]);
        Assert.DoesNotContain(lines, l => l.EndsWith(" best", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunWithNoResultsYetSaysNeither()
    {
        var run = RankingTests.RunOf([RankingTests.Other("area/x", "X", TargetState.Refused, "locked")]);
        var lines = Lines(run);
        Assert.DoesNotContain("no target survived", lines);
        Assert.DoesNotContain("every fight died:", lines);
    }

    [Fact]
    public void BestFollowsTheMetricAsked()
    {
        // Ant earns less but kills faster.
        var run = RankingTests.RunOf([
            RankingTests.Finished("area/ant", "Ant", 10, seconds: 30),
            RankingTests.Finished("area/bat", "Bat", 30, seconds: 60),
        ]);
        Assert.EndsWith(" best", Lines(run)[3]);
        Assert.EndsWith(" best", Lines(run, Metrics.KillTime)[2]);
    }

    [Fact]
    public void TheHashLineIsTheSecondAndUnchangedByMarks()
    {
        var run = RankingTests.RunOf([RankingTests.Died("area/a", "A", 10), RankingTests.Finished("area/b", "B", 5)]);
        Assert.Matches("^results [0-9a-f]{12}$", Lines(run)[1]);
        Assert.Equal(Lines(run)[1], Lines(run, Metrics.KillTime)[1]);
    }

    [Theory]
    [InlineData(true, false, "a-died")]
    [InlineData(false, true, "b-died")]
    [InlineData(true, true, "both-died")]
    public void AComparisonRowPrintsWhichSideDiedInTheVerdictsPlace(bool diedA, bool diedB, string word)
    {
        var a = RankingTests.RunOf([
            diedA ? RankingTests.Died("area/one", "One", 100) : RankingTests.Finished("area/one", "One", 100),
        ]);
        var b = RankingTests.RunOf([
            diedB ? RankingTests.Died("area/one", "One", 150) : RankingTests.Finished("area/one", "One", 150),
        ]);
        var row = CompareRowText.Row(Differences.Of(a, b, Strength, TimeUnit.Hour).Single());
        Assert.EndsWith("  " + word, row);
        Assert.DoesNotContain("better", row);
        Assert.Contains("pct=50%", row);
    }

    [Fact]
    public void AComparisonRowBetweenSurvivorsKeepsItsVerdict()
    {
        var a = RankingTests.RunOf([RankingTests.Finished("area/one", "One", 100)]);
        var b = RankingTests.RunOf([RankingTests.Finished("area/one", "One", 150)]);
        Assert.EndsWith("  better", CompareRowText.Row(Differences.Of(a, b, Strength, TimeUnit.Hour).Single()));
    }

    private static TargetResult WithRisk(TargetResult result, long? minHp, int closeCalls)
    {
        foreach (var batch in result.Batches)
        {
            batch.MaxHit = 90;
            batch.MinHp = minHp;
            batch.CloseCalls = closeCalls;
        }
        return result;
    }

    [Fact]
    public void WithNoFightSurvivedCloseCallsSayNoFightSurvived()
    {
        var died = WithRisk(RankingTests.Died("area/a", "A", 10), null, 0);
        Assert.Equal(
            "risk: highest hit 90  lowest HP every fight died  close calls: no fight survived",
            ResultText.RiskLine(died, "risk")
        );
    }

    [Fact]
    public void WithSurvivedFightsCloseCallsKeepTheirShareAndRange()
    {
        // 1 close call in each of 30 batches of 10 trials.
        var survived = WithRisk(RankingTests.Finished("area/a", "A", 10), 40, 1);
        var line = ResultText.RiskLine(survived, "risk")!;
        Assert.StartsWith("risk: highest hit 90  lowest HP 40  close calls 10.0% (", line);
        Assert.EndsWith("WARNING: highest hit reaches lowest HP", line);
        Assert.DoesNotContain("no fight survived", line);
    }

    [Fact]
    public void AMonsterOnTaskByTheSetupNamesItsMaster()
    {
        var r = RankingTests.Finished("area/ant", "Ant", 10);
        r.Task = "Kuren";
        var first = Full(RankingTests.RunOf([r]), r).Split('\n')[0];
        Assert.StartsWith("area/ant finished", first);
        Assert.EndsWith(" on Kuren's task", first);
    }

    [Fact]
    public void AMonsterNoMasterGivesSaysSo()
    {
        var r = RankingTests.Finished("area/ant", "Ant", 10);
        r.TaskNote = Note.Of("no master gives it: off task");
        Assert.EndsWith(" no master gives it: off task", Full(RankingTests.RunOf([r]), r).Split('\n')[0]);
    }

    [Fact]
    public void AMonsterAsTheSaveSaysNeither() =>
        Assert.DoesNotContain(
            "task",
            Full(
                    RankingTests.RunOf([RankingTests.Finished("area/ant", "Ant", 10)]),
                    RankingTests.Finished("area/ant", "Ant", 10)
                )
                .Split('\n')[0],
            StringComparison.Ordinal
        );

    private static string Full(Run run, TargetResult r) =>
        ResultText.Details(run, r, TimeUnit.Hour, PriceTable.Empty, id => id, full: true);

    [Fact]
    public void FullPrintsTheFightCountsAfterTheRiskLine()
    {
        var r = RankingTests.Finished("area/ant", "Ant", 10);
        foreach (var b in r.Batches)
        {
            b.Fight = FightCountsTests.Sample();
            b.RanOver = 0;
        }
        r.Batches[0].RanOver = 1;
        var run = RankingTests.RunOf([r]);
        var lines = Full(run, r).Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("fights: ", StringComparison.Ordinal));
        Assert.True(at > 0);
        var batches = r.Batches.Count;
        Assert.Equal(
            $"fights: {r.Batches.Sum(b => b.Trials)} trials, 1 ran over, {250.5 * batches:0.0} s in fights",
            lines[at]
        );
        Assert.Equal(
            $"  you:   attacks {100 * batches} hits {40 * batches} damage {635 * batches} (attack {600 * batches} effect {30 * batches} reflect {5 * batches}) overkill {20 * batches}",
            lines[at + 1]
        );
        Assert.Equal(
            $"         resisted {70 * batches} ward {8 * batches} armour {3 * batches} ward-damage {2 * batches} enemy-healed {11 * batches}",
            lines[at + 2]
        );
        Assert.Equal(
            $"  enemy: attacks {90 * batches} hits {60 * batches} damage {1000 * batches} (attack {900 * batches} effect {100 * batches} reflect 0)",
            lines[at + 3]
        );
        Assert.Contains(
            lines,
            l =>
                l.StartsWith("  trials ", StringComparison.Ordinal)
                && l.EndsWith(" ran-over 1", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void FullSaysTheCountsAreNotRecordedForAnOlderRun()
    {
        var run = RunFile.Read(RunFileFormatTests.Format2Run);
        foreach (var r in run.Results.Values)
        {
            var text = Full(run, r);
            Assert.Contains("\nfights: not recorded", text, StringComparison.Ordinal);
            Assert.DoesNotContain(
                text.Split('\n'),
                l =>
                    l.StartsWith("  trials ", StringComparison.Ordinal)
                    && l.Contains("ran-over", StringComparison.Ordinal)
            );
        }
    }

    [Fact]
    public void TheSummaryLeavesTheFightsOut()
    {
        var r = RankingTests.Finished("area/ant", "Ant", 10);
        r.Batches[0].Fight = FightCountsTests.Sample();
        var text = ResultText.Details(
            RankingTests.RunOf([r]),
            r,
            TimeUnit.Hour,
            PriceTable.Empty,
            id => id,
            full: false
        );
        Assert.DoesNotContain("fights:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StalledTargetsComeAfterTheDiedOnesUnderTheirReasons()
    {
        var slowFar = StallGroupsTests.Stalled("area/slow-far", 10, 5, 80);
        var slowNear = StallGroupsTests.Stalled("area/slow-near", 10, 5, 10);
        foreach (var r in new[] { slowFar, slowNear })
            r.Reasons = [StallReasons.Note(r.Stall!)];
        var run = RankingTests.RunOf([
            slowFar,
            RankingTests.Died("area/died", "Died", 5),
            StallGroupsTests.Stalled("area/immune", 10, 0, 100),
            RankingTests.Finished("area/ok", "Ok", 10),
            slowNear,
            RankingTests.Other("area/old", "Old", TargetState.Unfinished, "a trial ran over the 300 s limit"),
        ]);
        var lines = Lines(run);
        Assert.StartsWith("area/ok finished", lines[2]);
        Assert.Equal("area/old unfinished a trial ran over the 300 s limit", lines[3]);
        Assert.Equal("every fight died:", lines[4]);
        Assert.StartsWith("area/died ", lines[5]);
        Assert.Equal("never hit:", lines[6]);
        Assert.StartsWith("area/immune unfinished", lines[7]);
        Assert.Equal("too slow:", lines[8]);
        Assert.Equal(
            "area/slow-near unfinished Too slow: 10% HP left after 5 minutes. 5 of 10 attacks landed, 10 damage a hit.",
            lines[9]
        );
        Assert.StartsWith("area/slow-far unfinished Too slow: 80% HP left", lines[10]);
        Assert.Equal(11, lines.Length);
    }

    [Fact]
    public void AFinishedTargetWithRanOverTrialsSaysHowMany()
    {
        var r = RankingTests.Finished("area/ant", "Ant", 10);
        foreach (var b in r.Batches)
            b.RanOver = 0;
        r.Batches[2].RanOver = 2;
        Assert.EndsWith(" ran-over 2/300 best", Lines(RankingTests.RunOf([r]))[2]);
    }

    [Fact]
    public void FullShowsAnUnfinishedTrialsCountsAndReason()
    {
        var r = StallGroupsTests.Stalled("area/slow", 10, 5, 40);
        var text = Full(RankingTests.RunOf([r]), r);
        Assert.Contains(
            "\nfights: 1 unfinished trial\n  you:   attacks 10 hits 5 damage 50",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\n  chance to hit 50.0%   damage per hit 10.0   damage per second none",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains("\n  Too slow: 40% HP left", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPrintsTheNumbersWithTheirRanges()
    {
        var r = FightMetricsTests.Counted("area/a", 40, 10, 2);
        var text = Full(RankingTests.RunOf([r]), r);
        Assert.Contains(
            "\n  chance to hit 40.0% [40.0, 40.0]   damage per hit 12.5 [12.5, 12.5]   damage per second 10.0 [10.0, 10.0]",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\n  enemy chance to hit 50.0% [50.0, 50.0]   damage per hit 20.0 [20.0, 20.0]   damage taken per second 2.0 [2.0, 2.0]",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\n  overkill 0.0% [0.0, 0.0]   attacks per kill 10.0 [10.0, 10.0], of which 6.0 [6.0, 6.0] missed",
            text,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("of which effects", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ran over ", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "a-unfinished")]
    [InlineData(false, true, "b-unfinished")]
    [InlineData(true, true, "both-unfinished")]
    public void AComparisonRowPrintsWhichSideStalledWithItsReason(bool stalledA, bool stalledB, string word)
    {
        TargetResult Side(bool stalled)
        {
            if (!stalled)
                return RankingTests.Finished("area/one", "One", 100);
            var r = StallGroupsTests.Stalled("area/one", 10, 0, 100);
            r.Reasons = [StallReasons.Note(r.Stall!)];
            return r;
        }
        var row = CompareRowText.Row(
            Differences
                .Of(RankingTests.RunOf([Side(stalledA)]), RankingTests.RunOf([Side(stalledB)]), Strength, TimeUnit.Hour)
                .Single()
        );
        Assert.Contains("  " + word + "  ", row);
        Assert.Contains("Never hit it: 0 of 10 attacks landed.", row);
        Assert.DoesNotContain("better", row);
    }

    [Fact]
    public void AComparisonRowNamesASideWithRanOverTrialsAndKeepsItsVerdict()
    {
        var b = RankingTests.Finished("area/one", "One", 150);
        foreach (var batch in b.Batches)
            batch.RanOver = 0;
        b.Batches[0].RanOver = 3;
        var row = CompareRowText.Row(
            Differences
                .Of(
                    RankingTests.RunOf([RankingTests.Finished("area/one", "One", 100)]),
                    RankingTests.RunOf([b]),
                    Strength,
                    TimeUnit.Hour
                )
                .Single()
        );
        Assert.EndsWith("  better  b-ran-over 3/300", row);
    }

    [Fact]
    public void NotSimulatedLinesFollowTheAssumptions()
    {
        var run = ResultsModelTests.SampleRun();
        var r = run.Results.Values.First(x => x.HasEstimates);
        Assert.DoesNotContain("not simulated:", Full(run, r), StringComparison.Ordinal);
        r.NotSimulated = [new("item:MelvorBase:LuckyCharm", "Lucky Charm", Note.Of("drops may be higher"))];
        var text = Full(run, r);
        Assert.Contains("\nnot simulated: Lucky Charm: drops may be higher", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("assumptions:", StringComparison.Ordinal)
                < text.IndexOf("not simulated:", StringComparison.Ordinal)
        );
    }
}
