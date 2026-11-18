using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ChangeBlocksTests
{
    [Theory]
    [InlineData("blocks=Kuren:JackOLantern,Scarecrow", "MelvorBase:JackOLantern,MelvorBase:Scarecrow")]
    [InlineData("blocks=Kuren:Scarecrow", "MelvorBase:Scarecrow")]
    [InlineData("blocks=Kuren:none", "none")]
    public void ShortFormsRoundTrip(string text, string value)
    {
        var change = Change.Parse(text);
        Assert.Equal(ChangeKind.Blocks, change.Kind);
        Assert.Equal("MelvorBase:Kuren", change.Subject);
        Assert.Equal(value, change.Value);
        Assert.Equal(text, change.ShortForm);
        Assert.Equal(change, Change.Parse(change.ShortForm));
    }

    [Theory]
    [InlineData("blocks=Kuren:Scarecrow,JackOLantern")]
    [InlineData("blocks=MelvorBase:Kuren:MelvorBase:JackOLantern,Scarecrow")]
    [InlineData("blocks=Kuren:JackOLantern,Scarecrow,Scarecrow")]
    public void EqualBlocksHaveOneForm(string text) =>
        Assert.Equal("blocks=Kuren:JackOLantern,Scarecrow", Change.Parse(text).ShortForm);

    [Fact]
    public void AnotherNamespaceKeepsEveryIdInFull()
    {
        var change = Change.Parse("blocks=Mod:Master:Mod:Beast");
        Assert.Equal("Mod:Master", change.Subject);
        Assert.Equal(["Mod:Beast"], change.Blocked);
        Assert.Equal("blocks=Mod:Master:Mod:Beast", change.ShortForm);
        Assert.Equal("Mod:Master", Change.Parse("blocks=Mod:Master:none").Subject);
    }

    [Theory]
    [InlineData("blocks=Kuren:A,B,C", "at most 2")]
    [InlineData("blocks=Kuren:", "write blocks=")]
    [InlineData("blocks=Kuren", "write blocks=")]
    [InlineData("blocks=Kuren:A,none", "none alone")]
    public void FormsThatArentBlocksAreRefused(string text, string error)
    {
        Assert.False(Change.TryParse(text, out _, out var why));
        Assert.Contains(error, why, StringComparison.Ordinal);
    }

    [Fact]
    public void AListHoldsOneBlocksChangePerMaster()
    {
        var list = ChangeList
            .Of([Change.Parse("blocks=Kuren:Scarecrow"), Change.Parse("level=Slayer:1")])
            .With(Change.Parse("blocks=Kuren:none"))
            .With(Change.Parse("blocks=Hans:Wyvern"));
        Assert.Equal("level=Slayer:1 blocks=Hans:Wyvern blocks=Kuren:none", list.ShortForm);
    }
}
