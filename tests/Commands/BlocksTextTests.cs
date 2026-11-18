using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class BlocksTextTests
{
    private static readonly Estimate Figure = new(5000, 4900, 5100, 30, "ratio");

    private static TargetResult Master() =>
        new()
        {
            Key = "master:MelvorBase:Kuren",
            Kind = TargetKind.Master,
            Master = new MasterFacts
            {
                Slots = [new BlockSlotFacts { Open = true }, new BlockSlotFacts { Open = false, TasksNeeded = 12 }],
            },
        };

    private static BlockChoice Choice(Estimate? gain, params string[] blocks) =>
        new([.. blocks.Select(b => "MelvorBase:Farm/MelvorBase:" + b)], Figure, gain, 0, 0, 2000, 1.25, true);

    private static string Name(string id) => Ids.Brief(id) == "Jack" ? "Jack O'Lantern" : Ids.Brief(id);

    [Fact]
    public void TheBestWithinOpenSlotsThenTheOneNeedingASlot()
    {
        var best = Choice(new Estimate(0.382, 0.35, 0.415, 30, "relative"), "Jack");
        var needs = Choice(new Estimate(0.5, 0.45, 0.55, 30, "relative"), "Jack", "Scarecrow") with
        {
            SlotsNeeded = 1,
            TasksNeeded = 12,
            Cost = 4000,
        };
        var text = BlocksText.Of(
            new BlockSuggestion("xp:MelvorBase:Slayer", [], 56, best, needs, false),
            Master(),
            "run-1",
            Name
        );
        Assert.Contains(
            "blocks master:Kuren by xp:MelvorBase:Slayer, from run-1 (blocks now: none; slots open 1; 56 choices)",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains("open slots: Jack O'Lantern   +38.2% [35, 41.5]", text, StringComparison.Ordinal);
        Assert.Contains("2000 coins · 1.25 h   safe to leave running", text, StringComparison.Ordinal);
        Assert.Contains(
            "needs slot 2 · 12 more tasks: Jack O'Lantern, Scarecrow   +50%",
            text,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("as good", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoGainBeyondTheRangeSaysAsGood()
    {
        var suggestion = new BlockSuggestion(
            "kills",
            [],
            3,
            Choice(new Estimate(0.01, -0.02, 0.04, 30, "relative")),
            null,
            true
        );
        Assert.Contains(
            "blocks: as good",
            BlocksText.Of(suggestion, Master(), "run-1", Name),
            StringComparison.Ordinal
        );
        Assert.Equal("blocks: as good", BlocksText.Short(suggestion));
    }

    [Fact]
    public void TheShortLineGivesTheGain() =>
        Assert.Equal(
            "best blocks: +38.2% [35, 41.5]",
            BlocksText.Short(
                new BlockSuggestion(
                    "kills",
                    [],
                    3,
                    Choice(new Estimate(0.382, 0.35, 0.415, 30, "relative"), "Jack"),
                    null,
                    false
                )
            )
        );
}
