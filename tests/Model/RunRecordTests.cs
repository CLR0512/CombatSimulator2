using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunRecordTests
{
    private static Run Sample()
    {
        var a = RankingTests.Finished("t:a", "A", 1);
        var b = RankingTests.Finished("t:b", "B", 1);
        var run = RankingTests.RunOf([a, b]);
        run.Id = "run-20261113-101500-1";
        run.Seed = 7;
        run.SeedSet = true;
        run.Trials = 500;
        run.Copies = 4;
        run.SimulatedIn = "host";
        run.Fresh = true;
        run.StopPrecision = 0.05;
        run.MainSkill = "melvorD:Strength";
        run.Limits = new TrialLimits { EnemySeconds = 120, DungeonSeconds = 900 };
        run.Setup = new Setup
        {
            Character = "Dev",
            Loadout = "2",
            LoadoutName = "my secret loadout",
            IgnoreRequirements = true,
            Changes = ChangeList.Parse("level=Strength:50 shop=AutoEat2:1"),
        };
        run.Name = "my secret run";
        run.SnapshotSha256 = "abc";
        run.SetupHash = "setup-1";
        run.GameBuild = "26316041";
        run.Cs2Mvid = "mvid-1";
        run.State = RunState.Finished;
        return run;
    }

    [Fact]
    public void RoundTripsEveryField()
    {
        var run = Sample();
        var record = RunRecord.From(run, "what-if", null, PairSeeds.Of(run), "3f9c0011aabb", "snapshots/x.save");

        var back = RunRecord.Read(Entry(record))!;

        Assert.Equal(run.Id, back.Id);
        Assert.Equal("what-if", back.Kind);
        Assert.Equal(["t:a", "t:b"], back.Targets);
        Assert.Equal(500, back.Trials);
        Assert.Equal(4, back.Copies);
        Assert.Equal("host", back.Where);
        Assert.Equal(120, back.Limits.EnemySeconds);
        Assert.Equal(900, back.Limits.DungeonSeconds);
        Assert.Equal(7UL, back.Seed);
        Assert.True(back.SeedSet);
        Assert.Null(back.TargetSeeds);
        Assert.True(back.Fresh);
        Assert.Equal(0.05, back.StopPrecision);
        Assert.Equal("melvorD:Strength", back.MainSkill);
        Assert.Equal("Dev", back.Setup.Character);
        Assert.Equal("2", back.Setup.Loadout);
        Assert.True(back.Setup.IgnoreRequirements);
        Assert.Equal(run.Setup.Changes, back.Setup.Changes);
        Assert.Equal("abc", back.SnapshotSha256);
        Assert.Equal("setup-1", back.SetupHash);
        Assert.Equal("26316041", back.GameBuild);
        Assert.Equal("mvid-1", back.Cs2Mvid);
        Assert.Equal("Finished", back.State);
        Assert.Equal("3f9c0011aabb", back.ResultHash);
        Assert.Equal("snapshots/x.save", back.Snapshot);
        Assert.Empty(back.Missing());
        Assert.Equal(run.Setup.Identity, back.RunSetup().Identity);
    }

    [Fact]
    public void NeverWritesTypedNames()
    {
        var run = Sample();
        var json = RunRecord.From(run, "run", null, PairSeeds.Of(run), null, null).ToJson().GetRawText();

        Assert.DoesNotContain("my secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparisonBKeepsTheSeedsItTookFromA()
    {
        var run = Sample();
        run.Results["t:a"].RunSeed = 11;
        var record = RunRecord.From(run, "comparison B", "run-a", PairSeeds.Of(run), null, null);

        var back = RunRecord.Read(Entry(record))!;

        Assert.Equal("run-a", back.PairedWith);
        Assert.Equal(new Dictionary<string, ulong> { ["t:a"] = 11 }, back.TargetSeeds);
    }

    [Fact]
    public void AReusedResultKeepsTheSeedItWasFoughtWith()
    {
        var run = Sample();
        run.Results["t:b"].ReusedFrom = new ReuseOrigin { RunId = "run-old", Seed = 3 };

        var record = RunRecord.From(run, "run", null, PairSeeds.Of(run), null, null);

        Assert.Equal(new Dictionary<string, ulong> { ["t:b"] = 3 }, record.TargetSeeds);
    }

    [Fact]
    public void AnUnfinishedRunHasNoResultHash()
    {
        var run = Sample();
        run.State = RunState.Error;

        Assert.Null(RunRecord.From(run, "run", null, PairSeeds.Of(run), "3f9c0011aabb", null).ResultHash);
    }

    [Fact]
    public void AnOlderReportsEntryHasNoRecord()
    {
        using var doc = JsonDocument.Parse(
            """{ "id": "run-20261109-165500-1", "kind": "run", "state": "Finished", "seed": 1, "targets": 147 }"""
        );

        Assert.Null(RunRecord.Read(doc.RootElement));
    }

    [Fact]
    public void SaysWhatARerunLacks()
    {
        using var doc = JsonDocument.Parse("""{ "request": { "id": "run-1", "targets": [] } }""");

        var record = RunRecord.Read(doc.RootElement)!;

        Assert.Equal(["targets", "seed", "snapshotSha256"], record.Missing());
    }

    private static JsonElement Entry(RunRecord record)
    {
        var entry = new Dictionary<string, object?> { ["id"] = record.Id, ["request"] = record.ToJson() };
        return JsonSerializer.SerializeToElement(entry);
    }
}
