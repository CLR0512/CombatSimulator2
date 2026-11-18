using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class NoteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-notes-" + Guid.NewGuid().ToString("N"));

    public NoteTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // Each note CS2 makes, in English, is the text CS2 stored before notes: result hashes and replies don't move.
    [Fact]
    public void EnglishIsTheTextCs2StoredBefore()
    {
        static void Add(Note note, string english) => Assert.Equal(english, note.English());
        Add(Note.Of("clean start (combat stopped, HP full)"), "clean start (combat stopped, HP full)");
        Add(Note.Of("time of day frozen at 12:00"), "time of day frozen at 12:00");
        Add(
            Note.Of("requirements ignored: {0}", "Attack 20 | Complete Golbin Forest"),
            "requirements ignored: Attack 20 | Complete Golbin Forest"
        );
        Add(Note.Of("cancelled after {0} of {1} trials", 12, 1000), "cancelled after 12 of 1000 trials");
        Add(
            Note.Of("a trial ran over the {0:0} s limit after {1} finished trials", 300.0, 7),
            "a trial ran over the 300 s limit after 7 finished trials"
        );
        Add(Note.Of("{0}: {1}", "InvalidOperationException", "no copy"), "InvalidOperationException: no copy");
        Add(
            Note.Of("{0}: blocked a side effect at {1}", "SaveGame", "Driver.Fight"),
            "SaveGame: blocked a side effect at Driver.Fight"
        );
        Add(Note.Of("{0}: not counted, missing {1}", "rare-events", "a, b"), "rare-events: not counted, missing a, b");
        Add(Note.Of("the simulator failed: {0}", "boom"), "the simulator failed: boom");
        Add(Note.Of("failed: {0}: {1}", "IOException", "disk"), "failed: IOException: disk");
        Add(Note.Of("cancelled: the mod was reloaded"), "cancelled: the mod was reloaded");
        Add(Note.Plural("{0} target", "{0} targets", 1, 1), "1 target");
        Add(Note.Plural("{0} target", "{0} targets", 5, 5), "5 targets");
    }

    [Fact]
    public void EnglishFillsPlaceholdersWithInvariantNumbers()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            Assert.Equal(
                "a trial ran over the 1800 s limit after 3 finished trials",
                Note.Of("a trial ran over the {0:0} s limit after {1} finished trials", 1800.0, 3).English()
            );
            Assert.Equal("value 1234.5", Note.Of("value {0}", 1234.5).English());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    [Fact]
    public void HostReasonsReadAsBefore()
    {
        Assert.Equal(
            "not enough free memory for one copy (needs about 1174 MB with 1124 MB free): close other programs, "
                + "or simulate in the game's process",
            MemoryReserve.OutOfMemoryReason(150L << 20, 1L << 30, 1124L << 20).English()
        );
    }

    [Fact]
    public void MetricLabelsReadAsBefore()
    {
        Assert.Equal("Kills", Metrics.Describe(Metrics.Kills).Label);
        Assert.Equal("XP: Attack", Metrics.Describe("xp:MelvorBase:Attack", id => id[(id.IndexOf(':') + 1)..]).Label);
        Assert.Equal(
            "Used: Bronze Arrows (ammo)",
            Metrics.Describe("used:ammo:MelvorBase:BronzeArrows", _ => "Bronze Arrows").Label
        );
        Assert.Equal("Used: Prayer points", Metrics.Describe("used:currency:MelvorBase:PrayerPoints").Label);
    }

    [Fact]
    public void JsonLeavesOutAbsentFields()
    {
        var json = JsonSerializer.Serialize(Note.Of("cancelled after {0} of {1} trials", 12, 1000), RunFile.Options);
        Assert.Equal("{\"msg\":\"cancelled after {0} of {1} trials\",\"args\":[12,1000]}", json);
        Assert.Equal("{\"msg\":\"no item loss on death\"}", JsonSerializer.Serialize(Note.Of("no item loss on death")));
    }

    [Fact]
    public void JsonRoundTripsEveryField()
    {
        var note = Note.Plural("{0} target was cancelled", "{0} targets were cancelled", 3, 3);
        var back = JsonSerializer.Deserialize<Note>(JsonSerializer.Serialize(note, RunFile.Options), RunFile.Options)!;
        Assert.Equal("3 targets were cancelled", back.English());
        Assert.Equal(3, back.Count);
        Assert.Equal(note.PluralMsg, back.PluralMsg);
        var mixed = Note.Of("{0} {1} {2}", "a", 2.5, true);
        var again = JsonSerializer.Deserialize<Note>(
            JsonSerializer.Serialize(mixed, RunFile.Options),
            RunFile.Options
        )!;
        Assert.Equal("a 2.5 True", again.English());
    }

    [Fact]
    public void AVerbatimNoteIsShownAsItIs()
    {
        var note = Note.Text("Kill {0} Golbins");
        Assert.True(note.Verbatim);
        Assert.Equal("Kill {0} Golbins", note.English());
        var back = JsonSerializer.Deserialize<Note>(JsonSerializer.Serialize(note, RunFile.Options), RunFile.Options)!;
        Assert.True(back.Verbatim);
        Assert.Equal("Kill {0} Golbins", back.English());
    }

    [Fact]
    public void ANoteNeedsPlainValues() => Assert.Throws<ArgumentException>(() => Note.Of("{0}", new object()));

    // A run saved before notes: its text is strings, and its header marks ignored requirements in the label.
    private const string Format1 = """
        {"format":1,
         "header":{"id":"run-20261107-093034-1","state":"finished","setup":"current (requirements ignored)",
                   "gameBuild":"26316041 0.11.4alpha","seed":1,"targets":1,
                   "startedAt":"2026-11-07T16:30:34Z","endedAt":"2026-11-07T16:30:41Z"},
         "run":{"id":"run-20261107-093034-1",
                "setup":{"character":"Dev","loadout":"current","loadoutName":null,"ignoreRequirements":true,
                         "identity":"Dev/current/ignore-req","label":"current (requirements ignored)"},
                "gameBuild":"26316041 0.11.4alpha","seed":1,"trials":1000,"copies":4,"simulatedIn":"host",
                "error":"the simulator stopped responding",
                "targets":["MelvorBase:HobgolbinCamp"],"state":"error",
                "startedAt":"2026-11-07T16:30:34Z","endedAt":"2026-11-07T16:30:41Z",
                "results":{"MelvorBase:HobgolbinCamp":{"key":"MelvorBase:HobgolbinCamp","name":"Hobgolbin Camp",
                   "kind":"dungeon","state":"cancelled","reasons":["the simulator stopped responding"],
                   "trialsDone":0,"gameSeconds":0,"realSeconds":0,"batches":[],
                   "assumptions":["requirements ignored: Kill 0 / 15 Hobgolbin in Combat","slayer off task"],
                   "unsupported":["SaveGame: blocked a side effect at x"],"fights":[]}}}}
        """;

    private string WriteGzip(string name, string json)
    {
        var path = Path.Combine(_dir, name + RunFile.Extension);
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(Encoding.UTF8.GetBytes(json));
        return path;
    }

    [Fact]
    public void AFormat1RunLoadsWithItsTextVerbatim()
    {
        var path = WriteGzip("run-20261107-093034-1", Format1);
        var run = RunFile.Read(path);
        var result = run.Results["MelvorBase:HobgolbinCamp"];
        Assert.All(result.Reasons.Concat(result.Assumptions).Concat(result.Unsupported), n => Assert.True(n.Verbatim));
        Assert.Equal("the simulator stopped responding", result.Reasons.Single().English());
        Assert.Equal(
            ["requirements ignored: Kill 0 / 15 Hobgolbin in Combat", "slayer off task"],
            result.Assumptions.Select(a => a.English())
        );
        Assert.True(run.Error!.Verbatim);
        Assert.Equal("the simulator stopped responding", run.Error.English());
        Assert.True(run.Setup.IgnoreRequirements);
        Assert.Equal("current", run.Setup.Name);
        Assert.Equal("current (requirements ignored)", run.Setup.Label);
    }

    [Fact]
    public void AFormat1HeaderMovesTheSuffixToTheFlag()
    {
        var header = RunFile.ReadHeader(WriteGzip("run-20261107-093034-1", Format1));
        Assert.Equal("current", header.Setup);
        Assert.True(header.IgnoreRequirements);
    }

    [Fact]
    public void AFormat2RunRoundTrips()
    {
        var run = new Run
        {
            Id = "run-20261107-100000-1",
            Setup = new Setup { Character = "Dev", IgnoreRequirements = true },
            Targets = ["MelvorBase:HobgolbinCamp"],
            State = RunState.Error,
            Error = Note.Of("the simulator closed unexpectedly (code {0})", 3),
        };
        run.Results["MelvorBase:HobgolbinCamp"] = new TargetResult
        {
            Key = "MelvorBase:HobgolbinCamp",
            Kind = TargetKind.Dungeon,
            State = TargetState.Cancelled,
            Reasons = [Note.Of("cancelled after {0} of {1} trials", 4, 1000)],
            Assumptions = [Note.Of("slayer off task"), Note.Text("Kill 0 / 15 Hobgolbin")],
        };
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        RunFile.Write(path, run);
        var back = RunFile.Read(path);
        var result = back.Results["MelvorBase:HobgolbinCamp"];
        Assert.Equal("cancelled after 4 of 1000 trials", result.Reasons.Single().English());
        Assert.False(result.Reasons.Single().Verbatim);
        Assert.Equal(["slayer off task", "Kill 0 / 15 Hobgolbin"], result.Assumptions.Select(a => a.English()));
        Assert.True(result.Assumptions[1].Verbatim);
        Assert.Equal("the simulator closed unexpectedly (code 3)", back.Error!.English());
        var header = RunFile.ReadHeader(path);
        Assert.Equal("current", header.Setup);
        Assert.True(header.IgnoreRequirements);
    }
}
