using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class NameMaskTests
{
    [Fact]
    public void ReplacesEachKindOfName()
    {
        var mask = NameMask.For([(2, "Slayer gear")], ["My preset"], ["Rings test"]);

        Assert.Equal(
            "A: <loadout 2>, B: <preset>, kept <name>",
            mask.Apply("A: Slayer gear, B: My preset, kept Rings test")
        );
    }

    [Fact]
    public void OnlyWholeWords()
    {
        var mask = NameMask.For([(1, "Mage")], [], []);

        Assert.Equal("Mages and <loadout 1>.", mask.Apply("Mages and Mage."));
    }

    [Fact]
    public void LongestFirst()
    {
        var mask = NameMask.For([(1, "Melee"), (2, "Melee max")], [], []);

        Assert.Equal("<loadout 2> vs <loadout 1>", mask.Apply("Melee max vs Melee"));
    }

    [Fact]
    public void CaseAsTyped()
    {
        var mask = NameMask.For([(1, "Ranged")], [], []);

        Assert.Equal("ranged <loadout 1>", mask.Apply("ranged Ranged"));
    }

    [Fact]
    public void BlankNamesAndNoNamesLeaveTextAlone()
    {
        Assert.Equal("Loadout 1", NameMask.For([(1, " ")], [""], []).Apply("Loadout 1"));
    }
}
