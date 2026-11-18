using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class BestMastersTests
{
    private const string L1 = "MelvorBase:Loadout1";
    private static readonly Goal SlayerXp = new("xp:MelvorBase:Slayer");

    private static TargetResult Part(string name, double seconds, int deaths = 0) =>
        MasterFiguresTests.Part(
            Ids.PartKey("Kuren", "MelvorBase:Farm/MelvorBase:" + name),
            seconds,
            100,
            deathsPerBatch: deaths,
            seed: 7
        );

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
                    .. Enumerable.Range(0, 2).Select(i => new BlockSlotFacts { Id = "s" + i, Open = i < openSlots }),
                ],
            },
        };

    private static BestMaster Best(params (string? Choice, TargetResult Master)[] results) =>
        BestMasters.For("master:MelvorBase:Kuren", results, LoadoutPicks.For(results, SlayerXp), SlayerXp)!;

    [Fact]
    public void LoadoutsArePickedThenTheSlowMonsterIsBlockedOverThem()
    {
        var best = Best(
            (null, Master(1, Part("Cow", 10), Part("Jack", 10), Part("Slug", 60))),
            (L1, Master(1, Part("Cow", 5), Part("Jack", 20), Part("Slug", 60)))
        );
        Assert.Equal("loadouts=Kuren:Cow@Loadout1", best.Loadouts!.ShortForm);
        Assert.Equal("blocks=Kuren:Slug", best.Blocks!.ShortForm);
        Assert.Equal(1, best.Swaps);
        Assert.True(best.Gain!.Low > 0);
        Assert.Null(best.Stuck);
    }

    [Fact]
    public void AMonsterBlockedOverItsPickedLoadoutKeepsNoLoadout()
    {
        var best = Best(
            (null, Master(1, Part("Cow", 10), Part("Jack", 10), Part("Slug", 60))),
            (L1, Master(1, Part("Cow", 5), Part("Jack", 20), Part("Slug", 40)))
        );
        Assert.Equal("blocks=Kuren:Slug", best.Blocks!.ShortForm);
        Assert.Equal("loadouts=Kuren:Cow@Loadout1", best.Loadouts!.ShortForm);
        Assert.Equal(1, best.Swaps);
    }

    [Fact]
    public void WithNoSlotOpenTheBlocksStayAndLoadoutsStillHelp()
    {
        var best = Best(
            (null, Master(0, Part("Cow", 10), Part("Slug", 60))),
            (L1, Master(0, Part("Cow", 5), Part("Slug", 60)))
        );
        Assert.Null(best.Blocks);
        Assert.NotNull(best.Loadouts);
        Assert.True(best.Gain!.Low > 0);
    }

    [Fact]
    public void TheRankingPutsTheBestFirstAndTheStuckLast()
    {
        var good = Best((null, Master(0, Part("Cow", 5))));
        var worse = Best((null, Master(0, Part("Cow", 20))));
        var stuck = good with { Master = "master:MelvorBase:Hans", Figure = null, Stuck = "x" };
        Assert.Equal([good, worse, stuck], BestMasters.Ranked([stuck, worse, good], SlayerXp));
    }
}
