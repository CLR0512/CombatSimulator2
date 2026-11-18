using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ModifierSearchTests
{
    private static readonly ModifierLine Sword = SheetDiffTests.Mod("Iron_Sword", "+12 Stab Accuracy Bonus");

    [Fact]
    public void SearchLooksAtTheSourceAndTheDescriptionIgnoringCase()
    {
        Assert.True(ModifierSearch.Matches(Sword, "sword"));
        Assert.True(ModifierSearch.Matches(Sword, "IRON"));
        Assert.True(ModifierSearch.Matches(Sword, "accur"));
        Assert.True(ModifierSearch.Matches(Sword, "+12"));
        Assert.False(ModifierSearch.Matches(Sword, "prayer"));
    }

    [Fact]
    public void EmptyTextMatchesEverything()
    {
        Assert.True(ModifierSearch.Matches(Sword, ""));
        Assert.True(ModifierSearch.Matches(Sword, "   "));
        Assert.True(ModifierSearch.Matches(Sword, null));
    }

    [Fact]
    public void SpacesAroundTheTextDontCount()
    {
        Assert.True(ModifierSearch.Matches(Sword, "  stab "));
    }
}
