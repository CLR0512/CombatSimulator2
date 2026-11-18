using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ResultsModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-tests-" + Guid.NewGuid().ToString("N"));

    public ResultsModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    internal static Run SampleRun(string id = "run-20261106-120000-1", DateTime? endedAt = null)
    {
        var run = new Run
        {
            Id = id,
            Setup = new Setup
            {
                Character = "Dev",
                Loadout = "current",
                IgnoreRequirements = false,
            },
            SnapshotSha256 = "abc123",
            GameBuild = "26316041 0.1.0",
            Seed = 18_446_744_073_709_551_615UL,
            Trials = 60,
            Copies = 4,
            Targets = ["MelvorBase:GolbinForest/MelvorBase:Golbin", "MelvorBase:HobgolbinCamp"],
            State = RunState.Finished,
            StartedAt = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc),
            EndedAt = endedAt ?? new DateTime(2026, 11, 6, 12, 0, 9, DateTimeKind.Utc),
        };
        var golbin = new TargetResult
        {
            Key = run.Targets[0],
            Name = "Golbin",
            Kind = TargetKind.Enemy,
            State = TargetState.Finished,
            TrialsDone = 60,
            GameSeconds = 360,
            RealSeconds = 1.5,
            Assumptions = [Note.Of("unlimited consumables"), Note.Of("levels held")],
        };
        var random = new Random(3);
        for (var b = 0; b < 30; b++)
        {
            var tally = new Tally
            {
                Trials = 2,
                Kills = 2 - (b % 7 == 0 ? 1 : 0),
                Deaths = b % 7 == 0 ? 1 : 0,
                GameSeconds = 10 + random.NextDouble() * 4,
            };
            tally.Xp["MelvorBase:Attack"] = 8 + b % 3;
            tally.Gained["MelvorBase:Bones"] = 2;
            tally.Currency["MelvorBase:GP"] = 5 + b;
            tally.Used["item:MelvorBase:Shrimp"] = b % 2;
            golbin.Batches.Add(tally);
        }
        var camp = new TargetResult
        {
            Key = run.Targets[1],
            Name = "Hobgolbin Camp",
            Kind = TargetKind.Dungeon,
            State = TargetState.Refused,
            Reasons = [Note.Text("Attack 20"), Note.Text("Complete Golbin Forest")],
        };
        camp.Fights.Add(
            new FightRecord
            {
                Trial = 0,
                Stage = 1,
                Enemy = "MelvorBase:Hobgolbin",
                GameSeconds = 12.5,
                DamageTaken = 40,
                Outcome = FightOutcome.Kill,
            }
        );
        run.Results[golbin.Key] = golbin;
        run.Results[camp.Key] = camp;
        return run;
    }

    private static string Json(Run run) => JsonSerializer.Serialize(run, RunFile.Options);

    [Fact]
    public void ARunRoundTripsUnchanged()
    {
        var run = SampleRun();
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        RunFile.Write(path, run);
        var back = RunFile.Read(path);
        Assert.Equal(Json(run), Json(back));
        Assert.Equal(run.Seed, back.Seed);
        Assert.Equal(2, back.Results.Count);
        Assert.Single(back.Results[run.Targets[1]].Fights);
    }

    private static void AddSpeedFields(Run run)
    {
        run.Fresh = true;
        run.SeedSet = true;
        run.StopPrecision = 0.05;
        run.Timings = new RunTimings
        {
            Snapshot = 0.0012,
            ReuseLookup = 0.004,
            HostWait = 0.5,
            Transfer = 0.002,
            Check = 0.03,
            Builds = 4,
            BuildsSummed = 4.4,
            FromStock = 3,
            PrepareSummed = 0.2,
            Resets = 29,
            ResetMisses = 1,
            ResetSummed = 0.15,
            FightSummed = 14.2,
            Report = 0.02,
            FirstResult = 0.9,
            Total = 4.8,
            Reused = 2,
        };
        var golbin = run.Results[run.Targets[0]];
        golbin.StoppedEarly = true;
        golbin.ReusedFrom = new ReuseOrigin
        {
            RunId = "run-20261105-100000-1",
            Seed = 7,
            Date = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc),
        };
    }

    [Fact]
    public void TheSpeedFieldsRoundTrip()
    {
        var run = SampleRun();
        AddSpeedFields(run);
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        RunFile.Write(path, run);
        var back = RunFile.Read(path);
        Assert.Equal(Json(run), Json(back));
        Assert.Equal(29, back.Timings!.Resets);
        Assert.Equal(0.05, back.StopPrecision);
        var golbin = back.Results[run.Targets[0]];
        Assert.True(golbin.StoppedEarly);
        Assert.Equal("run-20261105-100000-1", golbin.ReusedFrom!.RunId);
        Assert.Equal(7UL, golbin.ReusedFrom.Seed);
        Assert.Equal(DateTimeKind.Utc, golbin.ReusedFrom.Date.Kind);
    }

    [Fact]
    public void AFormat2FileWithoutTheSpeedFieldsReadsThemAsUnset()
    {
        var run = SampleRun();
        // A run as files were written before these fields existed.
        var node = System.Text.Json.Nodes.JsonNode.Parse(Json(run))!.AsObject();
        foreach (var name in new[] { "timings", "fresh", "seedSet", "stopPrecision" })
            node.Remove(name);
        foreach (var (_, result) in node["results"]!.AsObject())
        {
            result!.AsObject().Remove("reusedFrom");
            result.AsObject().Remove("stoppedEarly");
        }
        var old = node.ToJsonString();
        Assert.DoesNotContain("timings", old, StringComparison.Ordinal);
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        var text = $"{{\"format\":2,\"header\":{RunFile.HeaderJson(run)},\"run\":{old}}}";
        using (var file = File.Create(path))
        using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
            gzip.Write(System.Text.Encoding.UTF8.GetBytes(text));
        var back = RunFile.Read(path);
        Assert.Null(back.Timings);
        Assert.False(back.Fresh);
        Assert.False(back.SeedSet);
        Assert.Null(back.StopPrecision);
        Assert.All(
            back.Results.Values,
            r =>
            {
                Assert.Null(r.ReusedFrom);
                Assert.False(r.StoppedEarly);
            }
        );
    }

    [Fact]
    public void TheSpeedFieldsLeaveTheResultsHashInputAlone()
    {
        var run = SampleRun();
        var before = ResultsHashInput.Of(run);
        AddSpeedFields(run);
        Assert.Equal(before, ResultsHashInput.Of(run));
    }

    [Fact]
    public void TheFileIsGzipJsonWithFormat3()
    {
        var run = SampleRun();
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        RunFile.Write(path, run);
        using var gzip = new System.IO.Compression.GZipStream(
            File.OpenRead(path),
            System.IO.Compression.CompressionMode.Decompress
        );
        using var doc = JsonDocument.Parse(gzip);
        Assert.Equal(3, doc.RootElement.GetProperty("format").GetInt32());
        Assert.Equal("finished", doc.RootElement.GetProperty("run").GetProperty("state").GetString());
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void AnotherFormatIsRefused()
    {
        var path = Path.Combine(_dir, "run-x" + RunFile.Extension);
        using (var file = File.Create(path))
        using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
            gzip.Write("{\"format\":4,\"run\":{}}"u8);
        var e = Assert.Throws<RunFileException>(() => RunFile.Read(path));
        Assert.Contains("format 4", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EstimatesFromStoredBatchesMatchThoseBeforeSaving()
    {
        var run = SampleRun();
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        var before = KillsPerSecond(run.Results[run.Targets[0]]);
        RunFile.Write(path, run);
        var after = KillsPerSecond(RunFile.Read(path).Results[run.Targets[0]]);
        Assert.Equal(before, after);
    }

    private static Estimate KillsPerSecond(TargetResult result) =>
        Estimators.RatioOfBatches(
            [.. result.Batches.Select(b => (double)b.Kills)],
            [.. result.Batches.Select(b => b.GameSeconds)]
        );

    [Theory]
    [InlineData(TargetState.Unfinished)]
    [InlineData(TargetState.Refused)]
    [InlineData(TargetState.Cancelled)]
    [InlineData(TargetState.Unsupported)]
    public void NonFinishedFinalStatesNeedAReasonAndHaveNoEstimates(TargetState state)
    {
        var bare = new TargetResult { Key = "k", State = state };
        Assert.NotEmpty(bare.Problems());
        Assert.False(bare.HasEstimates);

        var reasoned = new TargetResult
        {
            Key = "k",
            State = state,
            Reasons = [Note.Text("why")],
        };
        Assert.Empty(reasoned.Problems());
        Assert.False(reasoned.HasEstimates);
        Assert.True(reasoned.IsFinal);
    }

    [Fact]
    public void AFinishedResultHasEstimates()
    {
        var result = SampleRun().Results.Values.First(r => r.State == TargetState.Finished);
        Assert.True(result.HasEstimates);
        Assert.Empty(result.Problems());
    }

    [Fact]
    public void WritingARunWithAReasonlessRefusalFails()
    {
        var run = SampleRun();
        run.Results[run.Targets[1]].Reasons.Clear();
        Assert.Throws<RunFileException>(() => RunFile.Write(Path.Combine(_dir, "bad" + RunFile.Extension), run));
    }

    [Theory]
    [InlineData(TimeUnit.Second, 1)]
    [InlineData(TimeUnit.Minute, 60)]
    [InlineData(TimeUnit.Hour, 3600)]
    [InlineData(TimeUnit.Day, 86400)]
    public void TimeUnitsScaleSeconds(TimeUnit unit, double seconds)
    {
        Assert.Equal(seconds, TimeUnits.Seconds(unit));
        var tally = new Tally { GameSeconds = 7200, Kills = 40 };
        Assert.Equal(7200 / seconds, TimeUnits.Denominator(tally, unit), 9);
    }

    [Fact]
    public void PerKillUsesKillsAsTheDenominator()
    {
        var tally = new Tally { GameSeconds = 7200, Kills = 40 };
        Assert.Equal(40, TimeUnits.Denominator(tally, TimeUnit.Kill));
    }

    [Fact]
    public void RunIdsAreUniqueWithinASecond()
    {
        var now = new DateTime(2026, 11, 6, 13, 5, 9);
        var first = RunIds.Next(now, []);
        Assert.Equal("run-20261106-130509-1", first);
        Assert.Equal("run-20261106-130509-2", RunIds.Next(now, [first]));
    }

    [Fact]
    public void TallyAddSumsTheFightCountsAndRanOverTrials()
    {
        var sum = new Tally { Fight = FightCountsTests.Sample(), RanOver = 1 };
        sum.Add(new Tally { Fight = FightCountsTests.Sample(2), RanOver = 2 });
        Assert.Equal(3, sum.RanOver);
        Assert.Equal(FightCountsTests.Sample(3).Dealt.Attacks, sum.Fight!.Dealt.Attacks);
        Assert.Equal(FightCountsTests.Sample(3).Seconds, sum.Fight.Seconds);
    }

    [Fact]
    public void TallyAddIntoAnEmptyTallyCopiesTheCounts()
    {
        var other = new Tally { Fight = FightCountsTests.Sample(), RanOver = 0 };
        var sum = new Tally();
        sum.Add(other);
        other.Fight.Clear();
        Assert.Equal(100, sum.Fight!.Dealt.Attacks);
        Assert.Equal(0, sum.RanOver);
    }

    [Fact]
    public void TallyAddWithoutCountsKeepsThemUnrecorded()
    {
        var sum = new Tally();
        sum.Add(new Tally { Trials = 1 });
        Assert.Null(sum.Fight);
        Assert.Null(sum.RanOver);
    }

    [Fact]
    public void TallyClearForgetsTheCounts()
    {
        var tally = new Tally { Fight = FightCountsTests.Sample(), RanOver = 4 };
        tally.Clear();
        Assert.Null(tally.Fight);
        Assert.Null(tally.RanOver);
    }

    [Fact]
    public void AResultHasFightCountsWhenItsFirstBatchHas()
    {
        var result = new TargetResult { State = TargetState.Finished };
        Assert.False(result.HasFightCounts);
        result.Batches.Add(new Tally { Trials = 1 });
        Assert.False(result.HasFightCounts);
        result.Batches[0].Fight = new FightCounts();
        Assert.True(result.HasFightCounts);
    }

    [Fact]
    public void AFightRecordKeepsItsNumbersAndAnOldOneHasNone()
    {
        var record = new FightRecord
        {
            Stage = 1,
            Enemy = "e",
            Outcome = FightOutcome.RanOver,
            Attacks = 9,
            Hits = 4,
            DamageDealt = 50,
            EnemyAttacks = 7,
            EnemyHits = 3,
        };
        var json = JsonSerializer.Serialize(record, RunFile.Options);
        Assert.Contains("\"outcome\":\"ranOver\"", json, StringComparison.Ordinal);
        Assert.Contains("\"damageDealt\":50", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<FightRecord>(json, RunFile.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, RunFile.Options));
        var old = JsonSerializer.Serialize(new FightRecord { Stage = 1, Enemy = "e" }, RunFile.Options);
        foreach (var name in new[] { "attacks", "hits", "damageDealt", "enemyAttacks", "enemyHits" })
            Assert.DoesNotContain($"\"{name}\"", old, StringComparison.Ordinal);
        foreach (var r in RunFile.Read(RunFileFormatTests.Format2Run).Results.Values)
            Assert.All(r.Fights, f => Assert.Null(f.Attacks));
    }
}
