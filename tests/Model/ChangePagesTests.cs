using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ChangePagesTests
{
    [Theory]
    [InlineData("pet=Bruno:on", "MelvorBase:Bruno", "on")]
    [InlineData("pet=Bruno:off", "MelvorBase:Bruno", "off")]
    [InlineData("pet=Mod:Pup:on", "Mod:Pup", "on")]
    public void APetIsTurnedOnOrOff(string text, string subject, string value)
    {
        var change = Change.Parse(text);
        Assert.Equal((ChangeKind.Pet, subject, value), (change.Kind, change.Subject, change.Value));
        Assert.Equal(text, change.ShortForm);
    }

    [Theory]
    [InlineData("bonfire=Oak", "MelvorBase:Oak", "MelvorBase:Oak")]
    [InlineData("bonfire=MelvorBase:Oak", "MelvorBase:Oak", "MelvorBase:Oak")]
    [InlineData("bonfire=Mod:Ember", "Mod:Ember", "Mod:Ember")]
    [InlineData("bonfire=out", "out", null)]
    public void ABonfireIsATierOrOut(string text, string value, string? tier)
    {
        var change = Change.Parse(text);
        Assert.Null(change.Subject);
        Assert.Equal(value, change.Value);
        Assert.Equal(tier, change.Tier);
        Assert.Null(change.Item);
        Assert.Equal(change, Change.Parse(change.ShortForm));
    }

    [Theory]
    [InlineData("technique=Climbing:40", 40, "technique=Climbing:40")]
    [InlineData("technique=MelvorBase:Climbing:007", 7, "technique=Climbing:7")]
    [InlineData("technique=Mod:Leaping:2", 2, "technique=Mod:Leaping:2")]
    public void ATechniqueIsALevel(string text, int level, string shortForm)
    {
        var change = Change.Parse(text);
        Assert.Equal(level, change.TechniqueLevel);
        Assert.Null(change.Level);
        Assert.Equal(shortForm, change.ShortForm);
    }

    [Fact]
    public void EachPageChangeIsOnePerKey()
    {
        var list = ChangeList.Parse("pet=Bruno:on bonfire=Oak technique=Climbing:4 pet=Fen:off");
        Assert.Equal("pet=Bruno:on pet=Fen:off bonfire=Oak technique=Climbing:4", list.ShortForm);
        Assert.Equal(
            "pet=Bruno:off pet=Fen:off bonfire=out technique=Climbing:4",
            list.With(Change.Parse("pet=Bruno:off")).With(Change.Parse("bonfire=out")).ShortForm
        );
        Assert.Throws<FormatException>(() => ChangeList.Parse("bonfire=Oak bonfire=out"));
        Assert.Throws<FormatException>(() => ChangeList.Parse("technique=Climbing:4 technique=Climbing:5"));
    }

    [Fact]
    public void PageChangesKeySetupsApart()
    {
        string Identity(string changes) =>
            new Setup { Character = "Chk", Changes = ChangeList.Parse(changes) }.Identity;
        Assert.Equal("Chk/current", Identity(""));
        Assert.Equal("Chk/current/changes:pet=Bruno:on", Identity("pet=Bruno:on"));
        Assert.NotEqual(Identity("bonfire=Oak"), Identity("bonfire=out"));
        Assert.NotEqual(Identity("technique=Climbing:40"), Identity("technique=Climbing:39"));
    }

    [Theory]
    [InlineData("pet=Bruno:maybe", "on or off")]
    [InlineData("pet=Bruno", "pet=<pet>")]
    [InlineData("pet=:on", "pet=<pet>")]
    [InlineData("bonfire=", "bonfire=<tier>")]
    [InlineData("bonfire=none", "bonfire=<tier>")]
    [InlineData("bonfire=A:B:C", "bonfire=<tier>")]
    [InlineData("technique=Climbing:0", "from 1 to 999")]
    [InlineData("technique=Climbing:1000", "from 1 to 999")]
    [InlineData("technique=Climbing:high", "from 1 to 999")]
    [InlineData("technique=Climbing", "technique=<technique>")]
    public void BadPageChangesAreErrors(string text, string says)
    {
        Assert.False(Change.TryParse(text, out _, out var error));
        Assert.Contains(says, error, StringComparison.Ordinal);
    }
}
