using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class AnswerTests
{
    private static AnswerKey Key() =>
        new()
        {
            Setup = "Dev/current",
            Gear = "aaaaaaaaaaaa",
            Levels = "bbbbbbbbbbbb",
            GameBuild = "26316041",
            ModBuild = "0.1.0",
            Goal = new Goal("xp:main"),
            Locks = ["MelvorBase:Weapon"],
            Excluded = ["MelvorBase:IronHelmet"],
            Share = 0.01,
            Seed = 1,
        };

    internal static Answer Sample()
    {
        var run = ResultsModelTests.SampleRun();
        var result = run.Results.Values.First(r => r.HasEstimates);
        var changes = ChangeList.Parse("equip=Weapon:IronBattleaxe equip=Helmet:IronHelmet stance=Slash");
        SetResult Set(ChangeList list) =>
            new()
            {
                Changes = list,
                Slots = [new("MelvorBase:Weapon", "Weapon", "MelvorBase:StoneSword", "Stone Sword", null, null)],
                Stance = "MelvorBase:Slash",
                Style = "Melee",
                Result = result,
                Same = [ChangeList.Parse("equip=Helmet:SteelHelmet")],
                Gain = new Estimate(5110, 4000, 6200, 30, "paired"),
                Percent = new Estimate(0.124, 0.098, 0.15, 30, "relative"),
                Assumes = ["MelvorBase:IronArrows"],
            };
        return new Answer
        {
            Kind = SearchKind.BestGear,
            Key = Key(),
            Target = result.Key,
            TargetName = result.Name,
            State = AnswerState.Found,
            Metric = "xp:MelvorBase:Attack",
            Current = Set(ChangeList.Empty),
            Best = Set(changes),
            Ties = [Set(changes)],
            ByStyle = [new StyleAnswer { Style = "Melee", Best = Set(changes) }],
            SkippedStyles = [new("Magic", Note.Of("no spell in the setup"))],
            Margin = 0.01,
            Possible = 41472,
            RuledOut = new Dictionary<string, double> { ["beaten"] = 41000, ["same"] = 310 },
            Simulated = 162,
            LeftOut = [new("equip=Weapon:OddStaff", Note.Text("Needs 40 Magic"))],
            Stages = [new(100, 162, 139, 23, 1.5), new(400, 23, 19, 4, 0.9)],
            Reran = true,
            BehindAtStage = 1,
            Seed = 77,
            Gains =
            [
                new DropGain
                {
                    Item = "MelvorBase:SteelHelmet",
                    Name = "Steel Helmet",
                    Slot = "MelvorBase:Helmet",
                    SlotName = "Helmet",
                    Displaced = ["Stone Helmet"],
                    Source = Note.Of("Shop"),
                    Changes = ChangeList.Parse("equip=Helmet:SteelHelmet"),
                    Result = result,
                    Gain = new Estimate(1, 0.5, 1.5, 30, "paired"),
                },
            ],
            NoGain = 58,
            OverLimit = 3,
            NotYetWearable =
            [
                new("MelvorBase:RunePlatebody", "Rune Platebody", Answers.OtherSource, [Note.Text("Defence 40")]),
            ],
            FoundAt = new DateTime(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc),
        };
    }

    [Fact]
    public void AnAnswerRoundTripsThroughTheRunFilesJson()
    {
        var json = JsonSerializer.Serialize(Sample(), RunFile.Options);
        var back = JsonSerializer.Deserialize<Answer>(json, RunFile.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, RunFile.Options));
        Assert.Equal(AnswerState.Found, back.State);
        Assert.Equal(Sample().Best!.Changes, back.Best!.Changes);
        Assert.Empty(Sample().Key.Changed(back.Key));
        Assert.Equal(Sample().Key.Locks, back.Key.Locks);
        Assert.Equal(5110, back.Gain!.Point);
        Assert.Equal("no spell in the setup", back.SkippedStyles[0].Why.English());
        Assert.Equal(77UL, back.Seed);
        Assert.Contains("\"state\":\"found\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGainIsTheBestsAndOnlyAFinishedSearchIsKept()
    {
        Assert.Null(new Answer { State = AnswerState.KeepCurrent }.Gain);
        Assert.True(new Answer { State = AnswerState.KeepCurrent }.Finished);
        Assert.True(new Answer { State = AnswerState.NothingToSearch }.Finished);
        Assert.False(new Answer { State = AnswerState.Cancelled }.Finished);
        Assert.False(new Answer { State = AnswerState.Failed }.Finished);
    }

    [Fact]
    public void AKeyThatMatchesHasNothingChanged()
    {
        Assert.Empty(Key().Changed(Key()));
        Assert.Empty(Key().Changed(Key() with { Seed = null }));
        Assert.Empty(Key().Changed(Key() with { Locks = ["MelvorBase:Weapon"] }));
    }

    [Fact]
    public void AKeySaysWhatChanged()
    {
        string[] Changed(AnswerKey now) => [.. Key().Changed(now).Select(n => n.English())];
        Assert.Equal(["gear changed"], Changed(Key() with { Gear = "cccccccccccc" }));
        Assert.Equal(["levels changed"], Changed(Key() with { Levels = "cccccccccccc" }));
        Assert.Equal(["game updated"], Changed(Key() with { GameBuild = "2" }));
        Assert.Equal(["goal changed"], Changed(Key() with { Goal = new Goal("kills") }));
        Assert.Equal(["goal changed"], Changed(Key() with { Goal = new Goal("xp:main", DeathLimit.SurvivedOnce) }));
        Assert.Equal(["locks changed"], Changed(Key() with { Locks = [] }));
        Assert.Equal(["excluded items changed"], Changed(Key() with { Excluded = ["MelvorBase:X"] }));
        Assert.Equal(["margin changed"], Changed(Key() with { Share = 0.02 }));
        Assert.Equal(["seed changed"], Changed(Key() with { Seed = 2 }));
        Assert.Equal(["setup changed", "styles changed"], Changed(Key() with { Setup = "Dev/x", AllStyles = true }));
    }
}
