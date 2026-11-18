using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class AnswerStoreFormat2Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static Estimate Point(double value) => new(value, value * 0.9, value * 1.1, 30, "ratio");

    internal static Answer SetupAnswer()
    {
        var answer = AnswerTests.Sample();
        answer.Key = answer.Key with { Kinds = ["prayers", "toolbelt"], IncludeUnowned = true, Supplies = "abc" };
        answer.Loadout = "Loadout 2";
        answer.Best!.Choices =
        [
            new ChoiceChange
            {
                Kind = ChoiceKind.Prayers,
                Name = "prayers",
                New = ["MelvorBase:RockSkin", "MelvorBase:SharpEye"],
                NewNames = ["Rock Skin", "Sharp Eye"],
                Mark = Mark.Locked(Note.Of("Prayer level {0}", 40)),
                Supplies = [new SupplyCost("used:currency:MelvorBase:PrayerPoints", Point(1240))],
            },
            new ChoiceChange
            {
                Kind = ChoiceKind.Toolbelt,
                Subject = "MelvorBase:CombatUtility1",
                Name = "CombatUtility1",
                New = ["MelvorBase:BoneCasing"],
                NewNames = ["Bone Casing"],
                Mark = Mark.NotInBank,
            },
        ];
        answer.Best.Supplies = [new SupplyCost("used:currency:MelvorBase:PrayerPoints", Point(1240))];
        answer.Best.DamageTaken = Point(3.5);
        answer.Alternatives = new Alternatives { Cheaper = answer.Ties[0] };
        answer.CloseCalls =
        [
            new CloseCall
            {
                Subject = "Ring",
                Kept = "Sapphire Ring",
                RunnerUp = "Ruby Ring",
                RunnerUpChanges = ChangeList.Parse("equip=RingSlot1:RubyRing"),
                Difference = new Estimate(0.006, -0.004, 0.016, 30, "paired"),
                Trials = 4000,
                Seed = 9,
                Outcome = CloseCallOutcome.Tie,
            },
        ];
        answer.LeftOutUnjudged = [new("prayer=ProtectItem:on", Note.Of("keeps an item on death"))];
        answer.WinsThroughGap = [Note.Of("keeps an item on death")];
        return answer;
    }

    [Fact]
    public void ASetupAnswerRoundTrips()
    {
        var json = JsonSerializer.Serialize(SetupAnswer(), RunFile.Options);
        var back = JsonSerializer.Deserialize<Answer>(json, RunFile.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, RunFile.Options));
        Assert.Equal(["prayers", "toolbelt"], back.Key.Kinds);
        Assert.Equal(MarkKind.Locked, back.Best!.Choices[0].Mark.Kind);
        Assert.Equal("Prayer level 40", back.Best.Choices[0].Mark.Needs!.English());
        Assert.Equal(CloseCallOutcome.Tie, back.CloseCalls[0].Outcome);
        Assert.Equal("Loadout 2", back.Loadout);
        Assert.NotNull(back.Alternatives!.Cheaper);
        Assert.Null(back.Alternatives.Safer);
        Assert.Contains("\"kind\":\"prayers\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoreWritesFormat2AndReadsItBack()
    {
        var store = new AnswerStore(_dir);
        Assert.Null(store.Load());
        store.Keep(SetupAnswer());
        using (var gzip = new GZipStream(File.OpenRead(store.FilePath), CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
            Assert.StartsWith("{\"format\":2,", reader.ReadToEnd(), StringComparison.Ordinal);
        var again = new AnswerStore(_dir);
        Assert.Null(again.Load());
        var found = again.Find(SearchKind.BestGear, SetupAnswer().Target, SetupAnswer().Key);
        Assert.NotNull(found);
        Assert.Empty(found.Value.Changed);
        Assert.Equal(2, found.Value.Answer.Best!.Choices.Count);
    }

    [Fact]
    public void AFormat1FileLoadsAsGearSearches()
    {
        var store = new AnswerStore(_dir);
        Assert.Null(store.Load());
        store.Keep(AnswerTests.Sample());
        // Rewrite the file as format 1 wrote it: no kinds, no unowned, no supplies hash.
        string json;
        using (var gzip = new GZipStream(File.OpenRead(store.FilePath), CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
            json = reader.ReadToEnd();
        json = json.Replace("{\"format\":2,", "{\"format\":1,", StringComparison.Ordinal)
            .Replace(",\"kinds\":[]", "", StringComparison.Ordinal)
            .Replace(",\"includeUnowned\":false", "", StringComparison.Ordinal)
            .Replace(",\"supplies\":\"\"", "", StringComparison.Ordinal);
        Assert.DoesNotContain("includeUnowned", json, StringComparison.Ordinal);
        using (var file = File.Create(store.FilePath))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
            gzip.Write(Encoding.UTF8.GetBytes(json));
        var old = new AnswerStore(_dir);
        Assert.Null(old.Load());
        var now = AnswerTests.Sample().Key with { Supplies = "anything" };
        var found = old.Find(SearchKind.BestGear, AnswerTests.Sample().Target, now);
        Assert.NotNull(found);
        Assert.Empty(found.Value.Answer.Key.Kinds);
        Assert.False(found.Value.Answer.Key.IncludeUnowned);
        // A gear search doesn't go out of date by prayers, food or the toolbelt.
        Assert.Empty(found.Value.Changed);
    }

    [Fact]
    public void KindsAndUnownedAreNamedWhenTheyDiffer()
    {
        var key = SetupAnswer().Key;
        Assert.Equal(["choices tried changed"], key.Changed(key with { Kinds = ["prayers"] }).Select(n => n.English()));
        Assert.Equal(
            ["choices tried changed"],
            key.Changed(key with { IncludeUnowned = false }).Select(n => n.English())
        );
        Assert.Equal(
            ["prayers, spells, food or toolbelt changed"],
            key.Changed(key with { Supplies = "def" }).Select(n => n.English())
        );
    }
}
