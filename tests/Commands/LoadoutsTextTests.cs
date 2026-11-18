using MelvorMods.CombatSimulator2.Commands;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class LoadoutsTextTests
{
    private static string Key(string name) => Ids.PartKey("Kuren", "MelvorBase:Farm/MelvorBase:" + name);

    private static readonly LoadoutPicksAnswer Answer = new(
        "master:MelvorBase:Kuren",
        new Goal("xp:main"),
        3,
        [
            new(
                Key("Golbin"),
                "MelvorBase:Loadout2",
                new(10, 9, 11, 30, ""),
                new(0.124, 0.101, 0.147, 30, ""),
                [],
                [],
                false
            ),
            new(Key("Scout"), null, new(9, 8, 10, 30, ""), null, ["MelvorBase:Loadout2"], [], false),
            new(Key("Cow"), null, null, null, [], ["MelvorBase:Loadout1"], true),
        ],
        new(20, 19, 21, 30, ""),
        new(0.068, 0.059, 0.077, 30, "")
    );

    private static string Name(string id) => id.Replace("MelvorBase:Loadout", "Loadout ", StringComparison.Ordinal);

    [Fact]
    public void EachMonsterHasItsPickGainAndTies()
    {
        var lines = LoadoutsText.Of(Answer, 1, Name, "B").Split('\n');
        Assert.Equal("master:Kuren · best loadouts · 3 choices · metric xp:main · seed 1", lines[0]);
        Assert.Contains("Farm/Golbin   Loadout 2", lines[1], StringComparison.Ordinal);
        Assert.EndsWith("+12.4% [10.1, 14.7]", lines[1], StringComparison.Ordinal);
        Assert.Contains("setup", lines[2], StringComparison.Ordinal);
        Assert.EndsWith("tie with Loadout 2", lines[2], StringComparison.Ordinal);
        Assert.EndsWith("stuck with every choice", lines[3], StringComparison.Ordinal);
        Assert.Equal("  master: +6.8% [5.9, 7.7] xp:main against the setup", lines[4]);
        Assert.Equal("  on side B: loadouts=Kuren:Golbin@Loadout2", lines[5]);
    }

    [Fact]
    public void EveryMonsterOnTheSetupSaysSo() =>
        Assert.EndsWith(
            "on side B: the setup for every monster",
            LoadoutsText.Of(Answer with { Picks = [Answer.Picks[1]] }, 1, Name, "B"),
            StringComparison.Ordinal
        );
}
