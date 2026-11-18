using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RankingTests
{
    private const string Strength = "xp:MelvorBase:Strength";

    // 30 identical batches of 10 trials, 10 kills each: every estimate's point is exact.
    internal static TargetResult Finished(string key, string name, double xp, double seconds = 60, int deaths = 0)
    {
        var result = new TargetResult
        {
            Key = key,
            Name = name,
            State = TargetState.Finished,
            TrialsDone = 300,
        };
        for (var b = 0; b < 30; b++)
        {
            var tally = new Tally
            {
                Trials = 10,
                Kills = 10,
                Deaths = deaths,
                GameSeconds = seconds,
            };
            tally.Xp["MelvorBase:Strength"] = xp;
            tally.Xp["MelvorBase:Hitpoints"] = xp / 3;
            result.Batches.Add(tally);
        }
        return result;
    }

    internal static TargetResult Other(string key, string name, TargetState state, string? reason = null)
    {
        var result = new TargetResult
        {
            Key = key,
            Name = name,
            State = state,
        };
        if (reason != null)
            result.Reasons.Add(Note.Text(reason));
        return result;
    }

    // Targets in the order given; keys listed in `queued` have no result yet.
    internal static Run RunOf(IEnumerable<TargetResult> results, params string[] queued)
    {
        var run = new Run { State = RunState.Running };
        foreach (var r in results)
        {
            run.Targets.Add(r.Key);
            run.Results[r.Key] = r;
        }
        run.Targets.AddRange(queued);
        return run;
    }

    [Fact]
    public void EstimatesComeFirstByFractionOfBestThenEveryOtherStateByName()
    {
        var run = RunOf(
            [
                Other("j", "Juniper", TargetState.Unsupported, "a missing dependency"),
                Finished("a", "Ant", 10),
                Other("i", "Imp", TargetState.Refused, "needs level 50"),
                Finished("b", "Bat", 30),
                Other("h", "Hag", TargetState.Cancelled, "cancelled"),
                Finished("c", "Charlie", 20),
                Other("g", "Ghost", TargetState.Unfinished, "a trial ran over 300 s"),
                Finished("d", "Alpha", 20),
                Other("e", "Eel", TargetState.Fighting),
                Other("e2", "Bee", TargetState.Building),
                Other("f2", "Zed", TargetState.Queued),
            ],
            "f"
        );
        var table = Ranking.Rank(run, Strength, TimeUnit.Hour);
        Assert.Equal(["b", "d", "c", "a", "e2", "e", "f", "f2", "g", "h", "i", "j"], table.Rows.Select(r => r.Key));
    }

    [Theory]
    [InlineData("killTime", true)]
    [InlineData("deathRate", true)]
    [InlineData("deaths", true)]
    [InlineData("used:item:MelvorBase:Shrimp", true)]
    [InlineData("used:currency:MelvorBase:PrayerPoints", true)]
    [InlineData("kills", false)]
    [InlineData("xp:MelvorBase:Strength", false)]
    [InlineData("value:MelvorBase:GP", false)]
    [InlineData("item:MelvorBase:Bones", false)]
    public void LowerIsBetterOnlyForCostsTimeAndDeaths(string key, bool lower) =>
        Assert.Equal(lower, Ranking.LowerIsBetter(key));

    [Fact]
    public void FractionOfBestForHigherIsBetter()
    {
        var run = RunOf([Finished("a", "A", 10), Finished("b", "B", 30), Other("x", "X", TargetState.Refused, "no")]);
        var rows = Ranking.Rank(run, Strength, TimeUnit.Hour).Rows.ToDictionary(r => r.Key);
        Assert.Equal(1, rows["b"].OfBest, 9);
        Assert.Equal(1 / 3.0, rows["a"].OfBest, 9);
        Assert.Equal(0, rows["x"].OfBest);
        Assert.Null(rows["x"].Estimate);
        Assert.Equal("no", rows["x"].Reason);
    }

    [Fact]
    public void FractionOfBestForLowerIsBetter()
    {
        // 3 s and 6 s per kill.
        var run = RunOf([Finished("slow", "Slow", 10, seconds: 60), Finished("fast", "Fast", 10, seconds: 30)]);
        var table = Ranking.Rank(run, Metrics.KillTime, TimeUnit.Hour);
        Assert.True(table.LowerIsBetter);
        Assert.Equal(["fast", "slow"], table.Rows.Select(r => r.Key));
        Assert.Equal(1, table.Rows[0].OfBest, 9);
        Assert.Equal(0.5, table.Rows[1].OfBest, 9);
        Assert.Equal(3, table.Best!.Value, 9);
    }

    [Fact]
    public void WithABestOfZeroLowerIsBetterBarsRunFromTheWorstToTheBest()
    {
        // No deaths, 1 and 2 deaths per batch: the safe one is full, the worst empty, the middle halfway, even
        // though the best is zero.
        var run = RunOf([
            Finished("safe", "Safe", 10),
            Finished("some", "Some", 10, deaths: 1),
            Finished("most", "Most", 10, deaths: 2),
        ]);
        var rows = Ranking.Rank(run, Metrics.Deaths, TimeUnit.Hour).Rows.ToDictionary(r => r.Key);
        Assert.Equal(1, rows["safe"].OfBest, 9);
        // A slow outlier doesn't squash the rest when the best is above zero: best / value as usual.
        var times = RunOf([
            Finished("fast", "Fast", 10, seconds: 30),
            Finished("slow", "Slow", 10, seconds: 60),
            Finished("crawl", "Crawl", 10, seconds: 6000),
        ]);
        var t = Ranking.Rank(times, Metrics.KillTime, TimeUnit.Hour).Rows.ToDictionary(r => r.Key);
        Assert.Equal(0.5, t["slow"].OfBest, 9);
        Assert.Equal(0.005, t["crawl"].OfBest, 9);
        Assert.Equal(0.5, rows["some"].OfBest, 9);
        Assert.Equal(0, rows["most"].OfBest, 9);
        var compare = Ranking.Compare(run, ["safe", "some"], TimeUnit.Hour).Single(r => r.Metric.Key == Metrics.Deaths);
        Assert.Equal(0, compare.Cells[1].OfBest, 9);
    }

    [Fact]
    public void RankCountsOnlyTargetsWithEstimates()
    {
        var run = RunOf(
            [Other("q", "Aardvark", TargetState.Fighting), Finished("a", "A", 10), Finished("b", "B", 30)],
            "z"
        );
        var table = Ranking.Rank(run, Strength, TimeUnit.Hour);
        Assert.Equal([1, 2, null, null], table.Rows.Select(r => r.Rank));
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void MedianIsOverTargetsWithEstimates()
    {
        var run = RunOf([
            Finished("a", "A", 10),
            Finished("b", "B", 30),
            Finished("c", "C", 20),
            Other("x", "X", TargetState.Cancelled, "cancelled"),
        ]);
        // 10, 20 and 30 XP per minute → per hour.
        var odd = Ranking.Rank(run, Strength, TimeUnit.Hour);
        Assert.Equal(1200, odd.Median!.Value, 6);
        Assert.Equal(1800, odd.Best!.Value, 6);
        run.Results["d"] = Finished("d", "D", 40);
        run.Targets.Add("d");
        Assert.Equal(1500, Ranking.Rank(run, Strength, TimeUnit.Hour).Median!.Value, 6);
        Assert.Null(Ranking.Rank(RunOf([]), Strength, TimeUnit.Hour).Median);
    }

    [Fact]
    public void DetailStripsPlaceEveryTargetFromWorstToBest()
    {
        // Strength 10, 30, 20, 29: worst 10 at the left, best 30 at the right.
        var run = RunOf([
            Finished("a", "A", 10),
            Finished("b", "B", 30),
            Finished("c", "C", 20),
            Finished("d", "D", 29),
        ]);
        DetailRow Row(string key) => Ranking.Details(run, key, TimeUnit.Hour).Single(d => d.Metric.Key == Strength);
        Assert.Equal(1, Row("b").Spot, 9);
        Assert.Equal(0.95, Row("d").Spot, 9);
        Assert.Equal(0.5, Row("c").Spot, 9);
        Assert.Equal(0, Row("a").Spot, 9);
        Assert.Equal([0, 0.5, 0.95], Row("b").Others.Order().Select(o => Math.Round(o, 9)));
        Assert.Equal(3, Row("b").Others.Count);

        // Tied values share a place.
        var tied = RunOf([Finished("a", "A", 30), Finished("b", "B", 30), Finished("c", "C", 10)]);
        foreach (var key in new[] { "a", "b" })
            Assert.Equal(1, Ranking.Details(tied, key, TimeUnit.Hour).Single(d => d.Metric.Key == Strength).Rank);
        Assert.Equal(3, Ranking.Details(tied, "c", TimeUnit.Hour).Single(d => d.Metric.Key == Strength).Rank);

        // One target, or all equal: everyone is at the best end.
        var alone = RunOf([Finished("a", "A", 10)]);
        Assert.Equal(1, Ranking.Details(alone, "a", TimeUnit.Hour).Single(d => d.Metric.Key == Strength).Spot, 9);
    }

    [Fact]
    public void QuartilesInterpolateBetweenValues()
    {
        Assert.Equal((0.25, 0.5, 0.75), Ranking.Quartiles([0, 0.25, 0.5, 0.75, 1]));
        Assert.Equal((0.25, 0.3, 0.45), Rounded(Ranking.Quartiles([0.6, 0.2, 0.3])));
        Assert.Equal((0.4, 0.4, 0.4), Ranking.Quartiles([0.4]));
        Assert.Equal((0, 0, 0), Ranking.Quartiles([]));
    }

    [Fact]
    public void ADetailRowsBoxCoversTheMiddleHalfOfAllTargets()
    {
        var run = RunOf([
            Finished("a", "A", 10),
            Finished("b", "B", 30),
            Finished("c", "C", 20),
            Finished("d", "D", 25),
            Finished("e", "E", 15),
        ]);
        var row = Ranking.Details(run, "a", TimeUnit.Hour).Single(d => d.Metric.Key == Strength);
        // Spots 0, 1, 0.5, 0.75, 0.25.
        Assert.Equal((0.25, 0.5, 0.75), Rounded(row.Box));
    }

    [Theory]
    [InlineData(20, 30, 10, 0.5)] // higher is better
    [InlineData(2, 1, 5, 0.75)] // lower is better: the best is the smaller
    [InlineData(0, 0, 4, 1)] // no deaths is the best end
    [InlineData(7, 7, 7, 1)] // all equal
    [InlineData(double.NaN, 1, 5, 0)]
    public void SpanRunsFromWorstToBest(double value, double best, double worst, double spot) =>
        Assert.Equal(spot, Ranking.Span(value, best, worst), 9);

    [Fact]
    public void DetailsLeaveOutMetricsThatAreZeroEverywhere()
    {
        var run = RunOf([Finished("a", "A", 10), Finished("b", "B", 30, deaths: 1), Finished("c", "C", 20)]);
        var details = Ranking.Details(run, "a", TimeUnit.Hour).ToDictionary(d => d.Metric.Key);
        Assert.Contains(Strength, details.Keys);
        Assert.Contains(Metrics.Deaths, details.Keys);
        Assert.Equal(3, details[Strength].Rank);
        Assert.Equal(3, details[Strength].Count);
        Assert.Equal(1 / 3.0, details[Strength].OfBest, 9);
        Assert.Equal(2 / 3.0, details[Strength].MedianOfBest, 9);

        var safe = RunOf([Finished("a", "A", 10), Finished("b", "B", 30)]);
        var keys = Ranking.Details(safe, "a", TimeUnit.Hour).Select(d => d.Metric.Key).ToList();
        Assert.DoesNotContain(Metrics.Deaths, keys);
        Assert.DoesNotContain(Metrics.DeathRate, keys);
        Assert.Contains(Strength, keys);
        Assert.Empty(Ranking.Details(safe, "missing", TimeUnit.Hour));
    }

    [Fact]
    public void CompareFindsTheBestPinAndTheOthersDeficit()
    {
        var run = RunOf([
            Finished("a", "A", 30),
            Finished("b", "B", 20),
            Finished("c", "C", 30),
            Other("x", "X", TargetState.Refused, "needs level 50"),
        ]);
        var table = Ranking.Compare(run, ["b", "a", "x"], TimeUnit.Hour);
        var xp = table.Single(r => r.Metric.Key == Strength);
        Assert.Equal(["b", "a", "x"], xp.Cells.Select(c => c.Key));
        Assert.True(xp.Cells[1].IsBest);
        Assert.False(xp.Cells[0].IsBest);
        Assert.Equal(-100 / 3.0, xp.Cells[0].DeltaPercent, 6);
        Assert.Equal(2 / 3.0, xp.Cells[0].OfBest, 9);
        Assert.Null(xp.Cells[2].Estimate);
        Assert.Equal("needs level 50", xp.Cells[2].Reason);

        var tie = Ranking.Compare(run, ["a", "c"], TimeUnit.Hour).Single(r => r.Metric.Key == Strength);
        Assert.All(tie.Cells, c => Assert.True(c.IsBest));
        Assert.All(tie.Cells, c => Assert.Equal(0, c.DeltaPercent, 9));
    }

    [Fact]
    public void TheMainSkillSentinelRanksByTheRunsMainSkill()
    {
        var run = RunOf([Finished("a", "A", 10), Finished("b", "B", 30)]);
        run.MainSkill = "MelvorBase:Hitpoints";
        var table = Ranking.Rank(run, Metrics.MainXp, TimeUnit.Hour);
        Assert.Equal("xp:MelvorBase:Hitpoints", table.Metric);
        Assert.Equal(600, table.Best!.Value, 6);
    }

    [Fact]
    public void DetailsCanLeaveOutWhatTheTargetNeverHas()
    {
        var a = Finished("a", "A", 10);
        var b = Finished("b", "B", 30);
        foreach (var tally in b.Batches)
            tally.Gained["MelvorBase:Bones"] = 2;
        var run = RunOf([a, b]);
        Assert.Contains(Ranking.Details(run, "a", TimeUnit.Hour), d => d.Metric.Key == "item:MelvorBase:Bones");
        var trimmed = Ranking.Details(run, "a", TimeUnit.Hour, skipTargetsZeros: true);
        Assert.DoesNotContain(trimmed, d => d.Metric.Key == "item:MelvorBase:Bones");
        Assert.Contains(trimmed, d => d.Metric.Key == Metrics.Kills);
        Assert.Contains(
            Ranking.Details(run, "b", TimeUnit.Hour, skipTargetsZeros: true),
            d => d.Metric.Key == "item:MelvorBase:Bones"
        );
    }

    // While a run is active, rows already shown with a result keep their order, and targets that finish later
    // follow them, however they rank; the rows without a result come after, as the ranking puts them.
    [Fact]
    public void WhileARunIsActiveShownRowsKeepTheirPlace()
    {
        var first = RunOf(
            [Finished("a", "Ant", 10), Finished("b", "Bat", 30), Other("c", "Charlie", TargetState.Fighting)],
            "d"
        );
        var shown = Ranking.Hold(Ranking.Rank(first, Strength, TimeUnit.Hour), []);
        Assert.Equal(["b", "a", "c", "d"], shown.Rows.Select(r => r.Key));

        var later = RunOf([
            Finished("a", "Ant", 10),
            Finished("b", "Bat", 30),
            Finished("c", "Charlie", 50),
            Finished("d", "Dog", 20),
        ]);
        var held = Ranking.Hold(Ranking.Rank(later, Strength, TimeUnit.Hour), [.. shown.Rows.Select(r => r.Key)]);
        Assert.Equal(["b", "a", "c", "d"], held.Rows.Select(r => r.Key));
        // Ranks are the ranking's own, whatever the order shown.
        Assert.Equal(1, held.Rows.Single(r => r.Key == "c").Rank);
    }

    // Once the run is final the view sorts once: the normal ranking of the same results delivered at once.
    [Fact]
    public void AFinalRunShowsTheNormalRanking()
    {
        var run = RunOf([
            Finished("a", "Ant", 10),
            Finished("b", "Bat", 30),
            Finished("c", "Charlie", 50),
            Finished("d", "Dog", 20),
        ]);
        var normal = Ranking.Rank(run, Strength, TimeUnit.Hour);
        Assert.Equal(["c", "b", "d", "a"], normal.Rows.Select(r => r.Key));
        Assert.Equal(normal.Rows, Ranking.Hold(normal, ["b", "a", "c", "d"], final: true).Rows);
    }

    // Narrowed to the targets shown, rows keep their places and the count is still the whole run's.
    [Fact]
    public void ANarrowedTableKeepsRanksAndTheRunsCount()
    {
        var run = RunOf([
            Finished("a", "Ant", 10),
            Finished("b", "Bat", 30),
            Finished("c", "Charlie", 50),
            Other("d", "Dog", TargetState.Fighting),
        ]);
        var narrowed = Ranking.Rank(run, Strength, TimeUnit.Hour).Narrow(new HashSet<string> { "a", "b", "d" });
        Assert.Equal(["b", "a", "d"], narrowed.Rows.Select(r => r.Key));
        Assert.Equal(new int?[] { 2, 3, null }, narrowed.Rows.Select(r => r.Rank));
        Assert.Equal(3, narrowed.Count);
        Assert.Equal(3, Ranking.Hold(narrowed, ["a"]).Count);
    }

    // Every trial died, with XP earned on the way down.
    internal static TargetResult Died(string key, string name, double xp, double seconds = 60) =>
        Finished(key, name, xp, seconds, deaths: 10);

    [Fact]
    public void ATargetThatDiedEveryTrialIsNeverRankedOrBest()
    {
        var run = RunOf([
            Died("spinner", "Spinner", 90),
            Finished("vines", "Vines", 30),
            Finished("ant", "Ant", 10, deaths: 3),
            Died("spider", "Spider", 60),
            Other("x", "X", TargetState.Refused, "locked"),
            Other("u", "U", TargetState.Unfinished, "a trial ran over 300 s"),
        ]);
        var table = Ranking.Rank(run, Strength, TimeUnit.Hour);
        // Ranked best first, then the died by the stat, then the rest.
        Assert.Equal(["vines", "ant", "spinner", "spider", "u", "x"], table.Rows.Select(r => r.Key));
        Assert.Equal(new int?[] { 1, 2, null, null, null, null }, table.Rows.Select(r => r.Rank));
        Assert.Equal([false, false, true, true, false, false], table.Rows.Select(r => r.Died));
        Assert.Equal("vines", table.BestKey);
        Assert.Equal(2, table.DiedCount);
        Assert.Equal(2, table.Count);
        // Some deaths don't unrank a target.
        Assert.False(table.Rows[1].Died);
    }

    [Fact]
    public void ADiedRowKeepsItsValueButSetsNoScale()
    {
        var run = RunOf([Died("spinner", "Spinner", 90), Finished("vines", "Vines", 30), Finished("ant", "Ant", 10)]);
        var table = Ranking.Rank(run, Strength, TimeUnit.Hour);
        var spinner = table.Rows.Single(r => r.Key == "spinner");
        Assert.Equal(5400, spinner.Estimate!.Point, 6);
        Assert.Equal(0, spinner.OfBest);
        Assert.Null(spinner.Reasons);
        // 30 and 10 XP a minute: the best, the median and the bars are the survivors'.
        Assert.Equal(1800, table.Best!.Value, 6);
        Assert.Equal(1200, table.Median!.Value, 6);
        Assert.Equal(1, table.Rows.Single(r => r.Key == "vines").OfBest, 9);
        Assert.Equal(1 / 3.0, table.Rows.Single(r => r.Key == "ant").OfBest, 9);
    }

    [Fact]
    public void WithNoDeathsEveryTargetIsRankedAsBefore()
    {
        var run = RunOf([Finished("a", "A", 10), Finished("b", "B", 30), Finished("c", "C", 20)]);
        var table = Ranking.Rank(run, Strength, TimeUnit.Hour);
        Assert.Equal(["b", "c", "a"], table.Rows.Select(r => r.Key));
        Assert.Equal("b", table.BestKey);
        Assert.Equal(0, table.DiedCount);
        Assert.Equal(3, table.Count);
    }

    [Fact]
    public void TiedTargetsGiveOneBestKeyInAStableOrder()
    {
        var run = RunOf([Finished("z", "Bat", 30), Finished("y", "Bat", 30), Finished("x", "Ant", 30)]);
        var table = Ranking.Rank(run, Strength, TimeUnit.Hour);
        Assert.Equal(["x", "y", "z"], table.Rows.Select(r => r.Key));
        Assert.Equal("x", table.BestKey);
        Assert.Equal("x", Ranking.Rank(run, Strength, TimeUnit.Hour).BestKey);
    }

    [Fact]
    public void ALowerIsBetterStatRanksSurvivorsAndOrdersTheDiedByIt()
    {
        // 6 s, 3 s, 1 s and 2 s per kill; the two fastest died every trial.
        var run = RunOf([
            Finished("slow", "Slow", 10, seconds: 60),
            Finished("fast", "Fast", 10, seconds: 30),
            Died("dead1", "Dead one", 10, seconds: 10),
            Died("dead2", "Dead two", 10, seconds: 20),
        ]);
        var table = Ranking.Rank(run, Metrics.KillTime, TimeUnit.Hour);
        Assert.Equal(["fast", "slow", "dead1", "dead2"], table.Rows.Select(r => r.Key));
        Assert.Equal("fast", table.BestKey);
        Assert.Equal(3, table.Best!.Value, 9);
        Assert.Equal(6, table.Worst!.Value, 9);
    }

    [Fact]
    public void WhenEveryTargetDiedNothingIsBest()
    {
        var run = RunOf([Died("a", "A", 10), Died("b", "B", 30), Other("x", "X", TargetState.Refused, "locked")]);
        var table = Ranking.Rank(run, Strength, TimeUnit.Hour);
        Assert.Null(table.BestKey);
        Assert.Null(table.Best);
        Assert.Null(table.Median);
        Assert.Equal(0, table.Count);
        Assert.Equal(2, table.DiedCount);
        Assert.Equal(["b", "a", "x"], table.Rows.Select(r => r.Key));
        Assert.All(table.Rows, r => Assert.Null(r.Rank));
    }

    [Fact]
    public void ANarrowedTableKeepsTheRunsBestAndCountsTheDiedItShows()
    {
        var run = RunOf([
            Finished("a", "Ant", 10),
            Finished("b", "Bat", 30),
            Died("c", "Charlie", 50),
            Died("d", "Dog", 40),
        ]);
        var narrowed = Ranking.Rank(run, Strength, TimeUnit.Hour).Narrow(new HashSet<string> { "a", "c" });
        Assert.Equal(["a", "c"], narrowed.Rows.Select(r => r.Key));
        Assert.Equal(new int?[] { 2, null }, narrowed.Rows.Select(r => r.Rank));
        Assert.Equal("b", narrowed.BestKey);
        Assert.Equal(1, narrowed.DiedCount);
        Assert.Equal(2, narrowed.Count);
    }

    // A target that turns out to have died every trial joins the died rows; it never takes a place among the
    // ranked rows being held.
    [Fact]
    public void AHeldTableKeepsADiedRowOutOfTheRankedOrder()
    {
        var first = RunOf([
            Finished("a", "Ant", 10),
            Finished("b", "Bat", 30),
            Other("c", "Charlie", TargetState.Fighting),
        ]);
        var shown = Ranking.Hold(Ranking.Rank(first, Strength, TimeUnit.Hour), []);
        Assert.Equal(["b", "a", "c"], shown.Rows.Select(r => r.Key));

        var later = RunOf([Finished("a", "Ant", 10), Finished("b", "Bat", 30), Died("c", "Charlie", 90)], "d");
        var held = Ranking.Hold(Ranking.Rank(later, Strength, TimeUnit.Hour), [.. shown.Rows.Select(r => r.Key)]);
        Assert.Equal(["b", "a", "c", "d"], held.Rows.Select(r => r.Key));
        Assert.True(held.Rows[2].Died);
        Assert.Null(held.Rows[2].Rank);
        Assert.Equal("b", held.BestKey);
        Assert.Equal(1, held.DiedCount);
    }

    [Fact]
    public void DetailsGiveADiedTargetNoPlaceAndTheRankedTargetsScale()
    {
        var run = RunOf([
            Finished("a", "A", 10),
            Finished("b", "B", 30),
            Finished("c", "C", 20),
            Died("dead", "Dead", 90),
        ]);
        var dead = Ranking.Details(run, "dead", TimeUnit.Hour).Single(d => d.Metric.Key == Strength);
        Assert.True(dead.Died);
        Assert.Equal(0, dead.Rank);
        Assert.Equal(3, dead.Count);
        Assert.Equal(0, dead.OfBest);
        Assert.Equal(5400, dead.Estimate.Point, 6);
        Assert.Equal([0, 0.5, 1], dead.Others.Order().Select(o => Math.Round(o, 9)));
        Assert.Equal((0.25, 0.5, 0.75), Rounded(dead.Box));

        // A ranked target is placed among the ranked only: the died one is not ahead of it.
        var best = Ranking.Details(run, "b", TimeUnit.Hour).Single(d => d.Metric.Key == Strength);
        Assert.False(best.Died);
        Assert.Equal(1, best.Rank);
        Assert.Equal(3, best.Count);
        Assert.Equal(1, best.OfBest, 9);
        Assert.Equal(2, best.Others.Count);

        var alone = RunOf([Died("dead", "Dead", 90)]);
        var only = Ranking.Details(alone, "dead", TimeUnit.Hour).Single(d => d.Metric.Key == Strength);
        Assert.Equal((0, 0, 0d), (only.Rank, only.Count, only.Spot));
        Assert.Empty(only.Others);
    }

    [Fact]
    public void ADiedPinIsNeverBestForAnyStat()
    {
        // The died pin has the highest XP and the fastest kills.
        var run = RunOf([
            Finished("a", "A", 30, seconds: 60),
            Finished("b", "B", 20, seconds: 60),
            Died("dead", "Dead", 90, seconds: 10),
        ]);
        var rows = Ranking.Compare(run, ["dead", "a", "b"], TimeUnit.Hour);
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.True(row.Cells[0].Died);
            Assert.False(row.Cells[0].IsBest);
            Assert.NotNull(row.Cells[0].Estimate);
            Assert.Equal((0d, 0d), (row.Cells[0].OfBest, row.Cells[0].DeltaPercent));
            Assert.False(row.Cells[1].Died);
        }
        var xp = rows.Single(r => r.Metric.Key == Strength);
        Assert.True(xp.Cells[1].IsBest);
        Assert.Equal(-100 / 3.0, xp.Cells[2].DeltaPercent, 6);
        Assert.Equal(2 / 3.0, xp.Cells[2].OfBest, 9);
    }

    [Fact]
    public void WhenEveryPinDiedNoCellIsBest()
    {
        var run = RunOf([Died("a", "A", 30), Died("b", "B", 20)]);
        var xp = Ranking.Compare(run, ["a", "b"], TimeUnit.Hour).Single(r => r.Metric.Key == Strength);
        Assert.All(xp.Cells, c => Assert.True(c.Died));
        Assert.All(xp.Cells, c => Assert.False(c.IsBest));
        Assert.All(xp.Cells, c => Assert.NotNull(c.Estimate));
        Assert.All(xp.Cells, c => Assert.Null(c.Reasons));
    }

    [Fact]
    public void TiedSurvivedPinsAreAllBestBesideADiedOne()
    {
        var run = RunOf([Finished("a", "A", 30), Finished("c", "C", 30), Died("dead", "Dead", 30)]);
        var xp = Ranking.Compare(run, ["a", "dead", "c"], TimeUnit.Hour).Single(r => r.Metric.Key == Strength);
        Assert.Equal([true, false, true], xp.Cells.Select(c => c.IsBest));
        Assert.Equal([false, true, false], xp.Cells.Select(c => c.Died));
    }

    // A what-if recorded in the game: with 10 Hitpoints and 1 Defence, two targets killed the player in every one
    // of 1,000 fights while earning the most XP, and the third was survived once.
    [Fact]
    public void TheRecordedProbeRanksItsOneSurvivedTargetBest()
    {
        var withChanges = RunFile.Read(TestPaths.Fixtures(Path.Combine("probe", "run-20261111-145148-1.json.gz")));
        var table = Ranking.Rank(withChanges, Metrics.MainXp, TimeUnit.Hour);
        const string Vines = "MelvorBase:GreenMangroves/MelvorBase:CreeperVines";
        const string Spider = "MelvorBase:Murkwoods/MelvorBase:MurkwoodSpider";
        const string Spinner = "MelvorBase:Murkwoods/MelvorBase:MurkwoodSpinner";
        Assert.Equal([Vines, Spinner, Spider], table.Rows.Select(r => r.Key));
        Assert.Equal(Vines, table.BestKey);
        Assert.Equal(new int?[] { 1, null, null }, table.Rows.Select(r => r.Rank));
        Assert.Equal(2, table.DiedCount);
        // The died targets' values are higher than the best's, and still shown.
        Assert.True(table.Rows[1].Estimate!.Point > table.Rows[0].Estimate!.Point);
        Assert.Equal(table.Rows[0].Estimate!.Point, table.Best!.Value);

        var asYouAre = RunFile.Read(TestPaths.Fixtures(Path.Combine("probe", "run-20261111-145146-1.json.gz")));
        var differences = Differences.Of(asYouAre, withChanges, Metrics.MainXp, TimeUnit.Hour).ToDictionary(d => d.Key);
        Assert.Equal("B died every fight", differences[Spider].Reason!.English());
        Assert.Equal("B died every fight", differences[Spinner].Reason!.English());
        Assert.Null(differences[Vines].Reason);
        Assert.Equal(Verdict.Worse, differences[Vines].Verdict);
    }

    private static (double, double, double) Rounded((double Low, double Median, double High) q) =>
        (Math.Round(q.Low, 9), Math.Round(q.Median, 9), Math.Round(q.High, 9));
}
