using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SkinChoiceTests
{
    private static readonly Note NotInstalled = Note.Of("mElvUI isn't installed");

    [Theory]
    // chosen, available, mElvUI's skin on → shown, how it is drawn
    [InlineData("base", true, false, "base", SkinDraw.GameTheme)]
    [InlineData("base", false, false, "base", SkinDraw.GameTheme)]
    [InlineData("base", true, true, "base", SkinDraw.BaseThemeUnderSkip)]
    [InlineData("base", false, true, "base", SkinDraw.BaseThemeUnderSkip)]
    [InlineData("melvui", true, false, "melvui", SkinDraw.FlatFromPalette)]
    [InlineData("melvui", true, true, "melvui", SkinDraw.StyledByMElvUI)]
    [InlineData("melvui", false, false, "base", SkinDraw.GameTheme)]
    [InlineData("melvui", false, true, "base", SkinDraw.BaseThemeUnderSkip)]
    public void ResolvesTheShownSkin(string chosen, bool available, bool skinOn, string shown, SkinDraw draw)
    {
        var result = SkinChoice.Resolve(chosen, available ? null : NotInstalled, skinOn);
        Assert.Equal(chosen, result.Chosen);
        Assert.Equal(shown, result.Shown);
        Assert.Equal(draw, result.Draw);
        if (chosen == shown)
            Assert.Null(result.Reason);
        else
            Assert.Equal(NotInstalled, result.Reason);
    }

    [Fact]
    public void AnUnknownChoiceResolvesAsBase()
    {
        var result = SkinChoice.Resolve("fancy", null, skinOn: false);
        Assert.Equal(SkinChoice.Base, result.Chosen);
        Assert.Equal(SkinChoice.Base, result.Shown);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData(SkinDraw.GameTheme, false)]
    [InlineData(SkinDraw.BaseThemeUnderSkip, false)]
    [InlineData(SkinDraw.FlatFromPalette, true)]
    [InlineData(SkinDraw.StyledByMElvUI, true)]
    public void TheLookFollowsTheShownSkin(SkinDraw draw, bool melvui) =>
        Assert.Equal(melvui, new SkinResolution("base", "base", draw, null).IsMElvUILook);
}
