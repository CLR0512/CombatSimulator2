using System.IO.Compression;
using System.Text;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunFileChangesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-changes-" + Guid.NewGuid().ToString("N"));

    public RunFileChangesTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string PathOf(string name) => Path.Combine(_dir, name + RunFile.Extension);

    private static string Unzipped(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // A format-2 file as runs were written before changes existed.
    private const string BeforeChanges = """
        {"format":2,"header":{"id":"run-1","state":"finished","setup":"current","ignoreRequirements":false,
        "gameBuild":"b","seed":1,"targets":0,"startedAt":"2026-11-06T12:00:00Z","endedAt":null},
        "run":{"id":"run-1","setup":{"character":"Dev","loadout":"current","loadoutName":null,"ignoreRequirements":false},
        "snapshotSha256":"","gameBuild":"b","seed":1,"trials":1000,"copies":0,"targets":[],"state":"finished",
        "startedAt":"2026-11-06T12:00:00Z","results":{}}}
        """;

    [Fact]
    public void AFileWrittenBeforeChangesReadsWithNone()
    {
        var path = PathOf("run-1");
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
            gzip.Write(Encoding.UTF8.GetBytes(BeforeChanges));
        var run = RunFile.Read(path);
        Assert.Null(run.Setup.Changes);
        Assert.Null(run.ChangeOutcomes);
        Assert.Equal("Dev/current", run.Setup.Identity);
        Assert.Null(RunFile.ReadHeader(path).Changes);
    }

    [Fact]
    public void ARunWithChangesRoundTrips()
    {
        var run = ResultsModelTests.SampleRun();
        run.Setup.Changes = ChangeList.Parse("level=Strength:60 equip=Weapon:Steel_Sword");
        run.ChangeOutcomes =
        [
            new ChangeOutcome(Change.Parse("level=Strength:60"), "50", ChangeState.Accepted, []),
            new ChangeOutcome(
                Change.Parse("equip=Weapon:Steel_Sword"),
                "MelvorBase:Iron_Sword",
                ChangeState.RequirementIgnored,
                [Note.Text("Requires Attack level 5")]
            ),
            new ChangeOutcome(
                Change.Parse("equip=Shield:Rune_Shield"),
                "none",
                ChangeState.Refused,
                [Note.Of("bank full")]
            ),
            new ChangeOutcome(Change.Parse("food=Shrimp"), "MelvorBase:Shrimp", ChangeState.NoChange, []),
            new ChangeOutcome(Change.Parse("prayer=Thick_Skin:on"), null, ChangeState.Refused, []),
        ];
        var path = PathOf(run.Id);
        RunFile.Write(path, run);
        var read = RunFile.Read(path);
        Assert.Equal(run.Setup.Changes, read.Setup.Changes);
        Assert.Equal(run.Setup.Identity, read.Setup.Identity);
        Assert.NotNull(read.ChangeOutcomes);
        Assert.Equal(run.ChangeOutcomes.Count, read.ChangeOutcomes.Count);
        for (var i = 0; i < run.ChangeOutcomes.Count; i++)
        {
            var (expected, actual) = (run.ChangeOutcomes[i], read.ChangeOutcomes[i]);
            Assert.Equal(expected.Change, actual.Change);
            Assert.Equal(expected.From, actual.From);
            Assert.Equal(expected.State, actual.State);
            Assert.Equal(expected.Reasons.Select(n => n.English()), actual.Reasons.Select(n => n.English()));
        }
        Assert.Equal("level=Strength:60 equip=Weapon:Steel_Sword", RunFile.ReadHeader(path).Changes);
    }

    // A single run, or a comparison's first side, may carry changes of every kind and state.
    [Fact]
    public void ARunWithSetupPageChangesRoundTrips()
    {
        var run = ResultsModelTests.SampleRun();
        run.Setup.Changes = ChangeList.Parse(
            "level=Defence:60 quest=MQ3:done shop=AutoEat2:1 guild=Attack:2 star=Terra_Minor_3:on "
                + "toolbelt=CombatUtility1:none"
        );
        run.ChangeOutcomes =
        [
            new ChangeOutcome(Change.Parse("level=Defence:60"), "50", ChangeState.Accepted, []),
            new ChangeOutcome(Change.Parse("quest=MQ3:done"), "open", ChangeState.Accepted, []),
            new ChangeOutcome(Change.Parse("shop=AutoEat2:1"), "0", ChangeState.Accepted, []),
            new ChangeOutcome(Change.Parse("guild=Attack:2"), "0", ChangeState.Accepted, []),
            new ChangeOutcome(
                Change.Parse("star=Terra_Minor_3:on"),
                "off",
                ChangeState.RequirementIgnored,
                [Note.Text("Needs 12 knowledge")]
            ),
            new ChangeOutcome(Change.Parse("toolbelt=CombatUtility1:none"), "none", ChangeState.NoChange, []),
            new ChangeOutcome(
                Change.Parse("shop=Gone:1"),
                null,
                ChangeState.NotInBuild,
                [Note.Of("not in this build")]
            ),
        ];
        var path = PathOf(run.Id);
        RunFile.Write(path, run);
        var read = RunFile.Read(path);
        Assert.Equal(run.Setup.Identity, read.Setup.Identity);
        Assert.Equal(
            run.ChangeOutcomes.Select(o => (o.Change, o.From, o.State)),
            read.ChangeOutcomes!.Select(o => (o.Change, o.From, o.State))
        );
        Assert.Equal(run.Setup.Changes.ShortForm, RunFile.ReadHeader(path).Changes);
        var text = Unzipped(path);
        Assert.Contains("requirementIgnored", text, StringComparison.Ordinal);
        Assert.Contains("notInBuild", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunWithoutChangesWritesNoChangeFields()
    {
        var run = ResultsModelTests.SampleRun();
        var path = PathOf(run.Id);
        RunFile.Write(path, run);
        var text = Unzipped(path);
        Assert.DoesNotContain("\"changes\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("changeOutcomes", text, StringComparison.Ordinal);

        run.Setup.Changes = ChangeList.Empty;
        RunFile.Write(path, run);
        Assert.Equal(text, Unzipped(path));
    }

    // A format-2 dungeon run as runs were written before risk, rare drops, names and pins were kept.
    private const string BeforeDepth = """
        {"format":2,"header":{"id":"run-2","state":"finished","setup":"current","ignoreRequirements":false,
        "gameBuild":"b","seed":1,"targets":1,"startedAt":"2026-11-06T12:00:00Z","endedAt":"2026-11-06T12:00:09Z"},
        "run":{"id":"run-2","setup":{"character":"Dev","loadout":"current","loadoutName":null,"ignoreRequirements":false},
        "snapshotSha256":"","gameBuild":"b","seed":1,"trials":1,"copies":0,"targets":["MelvorBase:HobgolbinCamp"],
        "state":"finished","startedAt":"2026-11-06T12:00:00Z","endedAt":"2026-11-06T12:00:09Z",
        "results":{"MelvorBase:HobgolbinCamp":{"key":"MelvorBase:HobgolbinCamp","name":"Hobgolbin Camp",
        "kind":"dungeon","state":"finished","reasons":[],"trialsDone":1,"gameSeconds":60,"realSeconds":0.1,
        "batches":[{"trials":1,"kills":1,"deaths":0,"gameSeconds":60,"xp":{},"gained":{},"currency":{},"used":{},
        "rareEvents":{}}],"assumptions":[],"unsupported":[],"fights":[{"trial":0,"stage":1,
        "enemy":"MelvorBase:Golbin","gameSeconds":12.5,"damageTaken":40,"outcome":"kill"}],"reusedFrom":null,
        "stoppedEarly":false}}}}
        """;

    // SHA-256 of what a run without the depth fields wrote, and of its results hash input, before they existed.
    private const string SampleFileSha = "a68eb9dbb3cb99e60e3adbd5c9f96d7a89a6958bbc8895b96f9af554df9d46e9";
    private const string SampleHashInputSha = "05c00fdc90b4cf2710ae566d6910f20a76f8b9b82beca07715ad5c382e548830";

    private static string Sha(byte[] bytes) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    [Fact]
    public void AFileWrittenBeforeDepthReadsWithNone()
    {
        var path = PathOf("run-2");
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
            gzip.Write(Encoding.UTF8.GetBytes(BeforeDepth));
        var run = RunFile.Read(path);
        var result = run.Results["MelvorBase:HobgolbinCamp"];
        var tally = Assert.Single(result.Batches);
        Assert.Null(tally.MaxHit);
        Assert.Null(tally.MinHp);
        Assert.Null(tally.CloseCalls);
        var fight = Assert.Single(result.Fights);
        Assert.Null(fight.MaxHit);
        Assert.Null(fight.MinHp);
        Assert.Equal(40, fight.DamageTaken);
        Assert.Null(result.Drops);
        Assert.Null(result.Stages);
        Assert.Null(run.Name);
        Assert.False(run.Pinned);
        var header = RunFile.ReadHeader(path);
        Assert.Null(header.Name);
        Assert.False(header.Pinned);
    }

    [Fact]
    public void ARunWithDepthFieldsRoundTrips()
    {
        var run = ResultsModelTests.SampleRun();
        run.Name = "Before the sword";
        run.Pinned = true;
        var golbin = run.Results[run.Targets[0]];
        golbin.Batches[0].MaxHit = 31;
        golbin.Batches[0].MinHp = 12;
        golbin.Batches[0].CloseCalls = 1;
        golbin.Drops =
        [
            new DropChance
            {
                Source = DropSource.Loot,
                Id = "MelvorBase:RainbowSlimeCoat",
                Chance = 0.0024,
                Per = ChancePer.Kill,
            },
            new DropChance
            {
                Source = DropSource.RareEvent,
                Id = "MelvorBase:Signet",
                Chance = 0.001,
                Per = ChancePer.MinuteInCombat,
                Unmet = [Note.Text("Requires Slayer level 50")],
            },
        ];
        var camp = run.Results[run.Targets[1]];
        camp.Stages = 5;
        camp.Fights[0].MaxHit = 9;
        camp.Fights[0].MinHp = 3;
        var path = PathOf(run.Id);
        RunFile.Write(path, run);
        var read = RunFile.Read(path);
        Assert.Equal("Before the sword", read.Name);
        Assert.True(read.Pinned);
        var tally = read.Results[run.Targets[0]].Batches[0];
        Assert.Equal((31L, 12L, 1), (tally.MaxHit, tally.MinHp, tally.CloseCalls));
        Assert.Null(read.Results[run.Targets[0]].Batches[1].MaxHit);
        var drops = read.Results[run.Targets[0]].Drops!;
        Assert.Equal(2, drops.Count);
        Assert.Equal(
            (DropSource.Loot, "MelvorBase:RainbowSlimeCoat", (double?)0.0024, ChancePer.Kill),
            (drops[0].Source, drops[0].Id, drops[0].Chance, drops[0].Per)
        );
        Assert.Empty(drops[0].Unmet);
        Assert.Equal(DropSource.RareEvent, drops[1].Source);
        Assert.Equal(ChancePer.MinuteInCombat, drops[1].Per);
        Assert.Equal("Requires Slayer level 50", Assert.Single(drops[1].Unmet).English());
        var readCamp = read.Results[run.Targets[1]];
        Assert.Equal(5, readCamp.Stages);
        Assert.Equal((9L, 3L), (readCamp.Fights[0].MaxHit, readCamp.Fights[0].MinHp));
        var header = RunFile.ReadHeader(path);
        Assert.Equal("Before the sword", header.Name);
        Assert.True(header.Pinned);
    }

    [Fact]
    public void ARunWithoutDepthFieldsWritesWhatItWroteBefore()
    {
        var run = ResultsModelTests.SampleRun();
        var path = PathOf(run.Id);
        RunFile.Write(path, run);
        var text = Unzipped(path);
        foreach (var name in new[] { "maxHit", "minHp", "closeCalls", "drops", "stages", "pinned", "fight", "ranOver" })
            Assert.DoesNotContain($"\"{name}\"", text, StringComparison.Ordinal);
        // Only the format number moved on since: new members are left out when they hold nothing.
        Assert.StartsWith("{\"format\":3,", text, StringComparison.Ordinal);
        var asFormat2 = "{\"format\":2," + text["{\"format\":3,".Length..];
        Assert.Equal(SampleFileSha, Sha(Encoding.UTF8.GetBytes(asFormat2)));
        Assert.Equal(SampleHashInputSha, Sha(ResultsHashInput.Of(run)));
    }

    [Fact]
    public void RiskJoinsTheResultsHashButDropsNamesAndPinsDoNot()
    {
        var run = ResultsModelTests.SampleRun();
        var before = ResultsHashInput.Of(run);
        run.Name = "named";
        run.Pinned = true;
        run.Results[run.Targets[0]].Drops = [new DropChance { Id = "MelvorBase:Coat", Chance = 0.001 }];
        run.Results[run.Targets[1]].Stages = 5;
        Assert.Equal(before, ResultsHashInput.Of(run));
        run.Results[run.Targets[0]].Batches[0].MaxHit = 7;
        Assert.NotEqual(before, ResultsHashInput.Of(run));
        run.Results[run.Targets[0]].Batches[0].MaxHit = null;
        run.Results[run.Targets[1]].Fights[0].MinHp = 2;
        Assert.NotEqual(before, ResultsHashInput.Of(run));
    }

    [Fact]
    public void TheOutcomesAreNotPartOfTheResultsHash()
    {
        var run = ResultsModelTests.SampleRun();
        var before = ResultsHashInput.Of(run);
        run.ChangeOutcomes = [new ChangeOutcome(Change.Parse("level=Strength:60"), "50", ChangeState.Accepted, [])];
        Assert.Equal(before, ResultsHashInput.Of(run));
    }
}
