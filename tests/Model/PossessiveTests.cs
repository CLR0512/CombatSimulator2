using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PossessiveTests
{
    [Theory]
    [InlineData("Vannah", "Vannah's")]
    [InlineData("Fells", "Fells'")]
    public void OfTakesABareApostropheAfterS(string name, string expected) =>
        Assert.Equal(expected, Possessive.Of(name));

    [Fact]
    public void NotesMendTheNamesPutIn()
    {
        Assert.Equal("Fells' tasks", Note.Of("{0}'s tasks", "Fells").English());
        Assert.Equal("Kuren's tasks", Note.Of("{0}'s tasks", "Kuren").English());
        Assert.Equal(
            "Golbins is not in Fells' pool",
            Note.Of("{0} is not in {1}'s pool", "Golbins", "Fells").English()
        );
    }

    [Fact]
    public void TextWithoutThePossessiveIsKept() =>
        Assert.Equal("Aufgaben von Fells", Possessive.Mend("Aufgaben von Fells", ["Fells"]));
}
