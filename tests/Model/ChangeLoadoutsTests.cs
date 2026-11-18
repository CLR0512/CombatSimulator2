using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ChangeLoadoutsTests
{
    [Fact]
    public void ALoadoutsChangeNamesEachMonstersLoadout()
    {
        var change = Change.Parse("loadouts=Kuren:Scarecrow@Loadout1,Golbin@Loadout2");
        Assert.Equal(ChangeKind.Loadouts, change.Kind);
        Assert.Equal("MelvorBase:Kuren", change.Subject);
        Assert.Equal(
            [new("MelvorBase:Golbin", "MelvorBase:Loadout2"), new("MelvorBase:Scarecrow", "MelvorBase:Loadout1")],
            change.Loadouts
        );
        Assert.Equal("loadouts=Kuren:Golbin@Loadout2,Scarecrow@Loadout1", change.ShortForm);
    }

    [Theory]
    [InlineData("loadouts=Kuren:Golbin@Loadout2")]
    [InlineData("loadouts=Kuren:Golbin@Loadout2,Scarecrow@Loadout1")]
    [InlineData("loadouts=Kuren:none")]
    [InlineData("loadouts=Mod:Master:Mod:Beast@MelvorBase:Loadout1")]
    [InlineData("loadouts=Mod:Master:none")]
    public void ShortFormsRoundTrip(string text)
    {
        var change = Change.Parse(text);
        Assert.Equal(text, change.ShortForm);
        Assert.Equal(change, Change.Parse(change.ShortForm));
    }

    [Theory]
    [InlineData("loadouts=Kuren:Scarecrow@Loadout1,Golbin@Loadout2")]
    [InlineData("loadouts=MelvorBase:Kuren:MelvorBase:Golbin@MelvorBase:Loadout2,Scarecrow@Loadout1")]
    public void EqualChoicesHaveOneForm(string text) =>
        Assert.Equal("loadouts=Kuren:Golbin@Loadout2,Scarecrow@Loadout1", Change.Parse(text).ShortForm);

    [Fact]
    public void AnotherNamespaceKeepsEveryIdInFull()
    {
        var change = Change.Parse("loadouts=Mod:Master:Mod:Beast@Loadout1");
        Assert.Equal("Mod:Master", change.Subject);
        Assert.Equal([new MonsterLoadout("Mod:Beast", "MelvorBase:Loadout1")], change.Loadouts);
        Assert.Equal("loadouts=Mod:Master:Mod:Beast@MelvorBase:Loadout1", change.ShortForm);
    }

    [Theory]
    [InlineData("loadouts=Kuren:Golbin@Loadout1,Golbin@Loadout2", "at most once")]
    [InlineData("loadouts=Kuren:Golbin@Loadout1,MelvorBase:Golbin@Loadout1", "at most once")]
    [InlineData("loadouts=Kuren:Golbin", "write loadouts=")]
    [InlineData("loadouts=Kuren:Golbin@", "write loadouts=")]
    [InlineData("loadouts=Kuren:@Loadout1", "write loadouts=")]
    [InlineData("loadouts=Kuren:Golbin@A@B", "write loadouts=")]
    [InlineData("loadouts=Kuren:", "write loadouts=")]
    [InlineData("loadouts=Kuren", "write loadouts=")]
    public void FormsThatArentLoadoutsAreRefused(string text, string error)
    {
        Assert.False(Change.TryParse(text, out _, out var why));
        Assert.Contains(error, why, StringComparison.Ordinal);
    }

    [Fact]
    public void AListHoldsOneLoadoutsChangePerMasterAfterTheBlocks()
    {
        var list = ChangeList
            .Parse("loadouts=Kuren:Golbin@Loadout1 blocks=Kuren:Scarecrow level=Slayer:1")
            .With(Change.Parse("loadouts=Kuren:Golbin@Loadout3"))
            .With(Change.Parse("loadouts=Hans:Wyvern@Loadout2"));
        Assert.Equal(
            "level=Slayer:1 blocks=Kuren:Scarecrow loadouts=Hans:Wyvern@Loadout2 loadouts=Kuren:Golbin@Loadout3",
            list.ShortForm
        );
    }

    [Fact]
    public void ASetupWithoutLoadoutsKeepsItsIdentity()
    {
        var plain = new Setup { Character = "Chk", Changes = ChangeList.Parse("blocks=Kuren:Golbin") };
        Assert.Equal("Chk/current/changes:blocks=Kuren:Golbin", plain.Identity);
        var withLoadouts = new Setup
        {
            Character = "Chk",
            Changes = ChangeList.Parse("blocks=Kuren:Golbin loadouts=Kuren:Scarecrow@Loadout1"),
        };
        Assert.Equal(
            "Chk/current/changes:blocks=Kuren:Golbin loadouts=Kuren:Scarecrow@Loadout1",
            withLoadouts.Identity
        );
    }
}
