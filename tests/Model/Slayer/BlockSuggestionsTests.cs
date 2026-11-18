using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class BlockSuggestionsTests
{
    private const string SlayerXp = "xp:MelvorBase:Slayer";

    private static TargetResult Part(string name, double seconds, double xp, int seed, int deaths = 0, int kills = 10)
    {
        var part = MasterFiguresTests.Part(
            Ids.PartKey("Kuren", "MelvorBase:Farm/MelvorBase:" + name),
            seconds,
            xp,
            killsPerBatch: kills,
            deathsPerBatch: deaths,
            seed: seed
        );
        foreach (var batch in part.Batches)
            batch.Currency["MelvorBase:SlayerCoins"] = batch.Kills * 10L;
        return part;
    }

    private static TargetResult Master(int openSlots, params TargetResult[] parts) =>
        new()
        {
            Key = "master:MelvorBase:Kuren",
            Kind = TargetKind.Master,
            State = TargetState.Finished,
            Parts = [.. parts],
            Blocked = [],
            Master = new MasterFacts
            {
                BlockCost = 2000,
                Currency = "MelvorBase:SlayerCoins",
                Slots =
                [
                    new BlockSlotFacts
                    {
                        Id = "S1",
                        Open = openSlots >= 1,
                        TasksNeeded = openSlots >= 1 ? 0 : 5,
                    },
                    new BlockSlotFacts
                    {
                        Id = "S2",
                        Open = openSlots >= 2,
                        TasksNeeded = openSlots >= 2 ? 0 : 12,
                    },
                ],
            },
        };

    // Ten monsters, two of them slow for little XP: blocking those two is the clear best.
    private static TargetResult Kuren(int openSlots) =>
        Master(
            openSlots,
            [
                .. Enumerable.Range(0, 8).Select(i => Part("Fast" + i, 10, 5, seed: i + 1)),
                Part("SlowA", 60, 2, seed: 20),
                Part("SlowB", 50, 2, seed: 21),
            ]
        );

    private static string Key(string name) => "MelvorBase:Farm/MelvorBase:" + name;

    [Fact]
    public void EveryChoiceOfNoneOneOrTwoIsWeighedAndTheBestWithinOpenSlotsWins()
    {
        var suggestion = BlockSuggestions.For(Kuren(openSlots: 2), SlayerXp, TimeUnit.Hour)!;
        Assert.Equal(56, suggestion.Choices);
        Assert.Equal([Key("SlowA"), Key("SlowB")], suggestion.Best!.Blocks);
        Assert.True(suggestion.Best.Gain!.Low > 0);
        Assert.False(suggestion.AsGood);
        Assert.Null(suggestion.NeedsSlot);
        Assert.Equal(4000, suggestion.Best.Cost);
        Assert.True(suggestion.Best.Hours > 0);
        Assert.True(suggestion.Best.SafeToLeave);
    }

    [Fact]
    public void WithOneSlotOpenTheBestUsesOneAndTheTwoBlockChoiceSaysWhatItNeeds()
    {
        var suggestion = BlockSuggestions.For(Kuren(openSlots: 1), SlayerXp, TimeUnit.Hour)!;
        Assert.Equal([Key("SlowA")], suggestion.Best!.Blocks);
        Assert.Equal([Key("SlowA"), Key("SlowB")], suggestion.NeedsSlot!.Blocks);
        Assert.Equal(1, suggestion.NeedsSlot.SlotsNeeded);
        Assert.Equal(12, suggestion.NeedsSlot.TasksNeeded);
    }

    [Fact]
    public void EqualMonstersLeaveTheBlocksAsGood()
    {
        var master = Master(2, [.. Enumerable.Range(0, 4).Select(i => Part("Same" + i, 10, 5, seed: i + 1))]);
        Assert.True(BlockSuggestions.For(master, SlayerXp, TimeUnit.Hour)!.AsGood);
    }

    [Fact]
    public void AStuckMonsterIsOnlyInChoicesThatBlockIt()
    {
        var master = Master(
            1,
            Part("Wolf", 140, 5, seed: 1),
            Part("Harpy", 340, 8, seed: 2, deaths: 3, kills: 0),
            Part("DireWolf", 300, 6, seed: 3)
        );
        var suggestion = BlockSuggestions.For(master, SlayerXp, TimeUnit.Hour)!;
        Assert.Contains(Key("Harpy"), suggestion.Best!.Blocks);
        Assert.Null(suggestion.Best.Gain);
        Assert.False(suggestion.AsGood);
    }

    [Fact]
    public void ABlockAlreadyMadeCostsNothing()
    {
        var master = Kuren(openSlots: 2);
        master.Blocked = [Key("SlowA")];
        var suggestion = BlockSuggestions.For(master, SlayerXp, TimeUnit.Hour)!;
        Assert.Equal([Key("SlowA")], suggestion.Current);
        Assert.Equal(2000, suggestion.Best!.Cost);
    }

    [Fact]
    public void DroppingAMonsterEqualsTheFiguresWithoutIt()
    {
        var master = Kuren(openSlots: 2);
        var without = master.Parts!.Where(p => !p.Key.EndsWith("SlowA", StringComparison.Ordinal)).ToList();
        master.Blocked = [Key("SlowA")];
        Assert.Equal(
            MasterFigures.Estimate(without, SlayerXp, TimeUnit.Hour)!.Point,
            Metrics.Estimate(master, SlayerXp, TimeUnit.Hour)!.Point,
            9
        );
    }
}
