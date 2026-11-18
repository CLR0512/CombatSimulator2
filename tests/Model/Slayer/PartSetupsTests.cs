using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PartSetupsTests
{
    private const string Kuren = "master:MelvorBase:Kuren";
    private const string Golbin = Kuren + "/MelvorBase:GolbinForest/MelvorBase:Golbin";
    private const string Scout = Kuren + "/MelvorBase:GolbinForest/MelvorBase:GolbinScout";
    private const string Cow = Kuren + "/MelvorBase:CorruptedFarmlands/MelvorBase:MagicCow";
    private const string Scarecrow = Kuren + "/MelvorBase:CorruptedFarmlands/MelvorBase:Scarecrow";

    private static Setup Side(string changes) => new() { Character = "Chk", Changes = ChangeList.Parse(changes) };

    [Fact]
    public void EachMonsterNamedWearsItsLoadoutAndTheRestTheSides()
    {
        var map = PartSetups.Of(
            Side(
                "equip=Weapon:Iron_Sword blocks=Kuren:Scarecrow loadouts=Kuren:Golbin@Loadout3,MagicCow@Loadout3,GolbinScout@Loadout1"
            ),
            [Golbin, Scout, Cow, Scarecrow]
        );
        Assert.Equal(
            new Dictionary<string, string>
            {
                [Golbin] = "MelvorBase:Loadout3",
                [Scout] = "MelvorBase:Loadout1",
                [Cow] = "MelvorBase:Loadout3",
            },
            map
        );
    }

    [Fact]
    public void AMonsterOfAnotherMasterOrAloneUsesTheSide()
    {
        var map = PartSetups.Of(
            Side("loadouts=Kuren:Golbin@Loadout1"),
            [
                "master:MelvorBase:Hans/MelvorBase:GolbinForest/MelvorBase:Golbin",
                "MelvorBase:GolbinForest/MelvorBase:Golbin",
            ]
        );
        Assert.Empty(map);
    }

    [Theory]
    [InlineData("")]
    [InlineData("equip=Weapon:Iron_Sword blocks=Kuren:Scarecrow")]
    [InlineData("loadouts=Kuren:none")]
    public void NoLoadoutPerMonsterGivesNone(string changes) =>
        Assert.Empty(PartSetups.Of(Side(changes), [Golbin, Cow]));
}
