using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class NotSimulatedTests
{
    private static readonly GapNote Protect = new(
        "prayer:MelvorBase:ProtectItem",
        "Protect Item",
        Note.Of("keeps an item on death; CS2 never takes one")
    );

    [Fact]
    public void AResultWithoutNotesWritesNone()
    {
        var json = JsonSerializer.Serialize(RankingTests.Finished("a", "A", 10), RunFile.Options);
        Assert.DoesNotContain("notSimulated", json, StringComparison.Ordinal);
        Assert.Null(JsonSerializer.Deserialize<TargetResult>(json, RunFile.Options)!.NotSimulated);
    }

    [Fact]
    public void NotesRoundTripWithTheirOwner()
    {
        var result = RankingTests.Finished("a", "A", 10);
        result.NotSimulated = [Protect];
        var json = JsonSerializer.Serialize(result, RunFile.Options);
        Assert.Contains("\"notSimulated\"", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<TargetResult>(json, RunFile.Options)!;
        var note = Assert.Single(back.NotSimulated!);
        Assert.Equal("prayer:MelvorBase:ProtectItem", note.Owner);
        Assert.Equal("keeps an item on death; CS2 never takes one", note.What.English());
    }

    [Fact]
    public void PartsShareANoteOnce()
    {
        var a = RankingTests.Finished("a", "A", 10);
        var b = RankingTests.Finished("b", "B", 10);
        var c = RankingTests.Finished("c", "C", 10);
        a.NotSimulated = [Protect];
        b.NotSimulated = [Protect with { What = Note.Of("keeps an item on death; CS2 never takes one") }];
        Assert.Single(GapNote.Merged([a, b, c])!);
        Assert.Null(GapNote.Merged([c]));
    }

    [Fact]
    public void AMasterTakesItsPartsNotesOnce()
    {
        var master = new TargetResult
        {
            Key = "master:Kuren",
            Kind = TargetKind.Master,
            Parts = [],
        };
        foreach (var key in new[] { "a", "b" })
        {
            var part = RankingTests.Finished(key, key, 10);
            part.NotSimulated = [Protect];
            master.Parts.Add(part);
        }
        MasterParts.Settle(master);
        Assert.Single(master.NotSimulated!);
    }
}
