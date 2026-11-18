using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SheetDiffTests
{
    internal static StatLine Stat(string key, double? value, StatGroup group = StatGroup.Offence) =>
        new(
            key,
            group,
            Note.Text(key),
            value,
            value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? key + "-text"
        );

    internal static ModifierLine Mod(
        string source,
        string description,
        ModifierGroup group = ModifierGroup.Equipment,
        ModifierSign kind = ModifierSign.Positive
    ) => new(group, "MelvorBase:" + source, source.Replace('_', ' '), description, kind, false);

    internal static SetupSheet Sheet(IEnumerable<StatLine> stats, IEnumerable<ModifierLine> modifiers) =>
        new(new Setup { Character = "Dev" }, "hash", "26316041", [.. stats], [.. modifiers], DateTime.UnixEpoch);

    [Fact]
    public void StatsAreMatchedByKeyWithTheDifference()
    {
        var a = Sheet([Stat("accuracy", 4812), Stat("max-hit", 151), Stat("style", null)], []);
        var b = Sheet([Stat("accuracy", 5100), Stat("max-hit", 151), Stat("style", null)], []);
        var diff = SheetDiff.Of(a, b);
        Assert.Equal(["accuracy", "max-hit", "style"], diff.Stats.Select(s => s.Key));
        Assert.Equal(288, diff.Stats[0].Delta);
        Assert.True(diff.Stats[0].Differs);
        Assert.Equal(0, diff.Stats[1].Delta);
        Assert.False(diff.Stats[1].Differs);
        Assert.Null(diff.Stats[2].Delta); // text only
        Assert.False(diff.Stats[2].Differs);
    }

    [Theory]
    [InlineData("max-hit", 102, 112, true)]
    [InlineData("max-hit", 112, 102, false)]
    [InlineData("attack-interval", 2.7, 3.0, false)]
    [InlineData("stat:MelvorBase:AttackInterval", 2700, 2400, true)]
    [InlineData("max-hit", 102, 102, null)]
    public void BIsBetterWithMoreExceptForAnInterval(string key, double a, double b, bool? better)
    {
        var diff = SheetDiff.Of(Sheet([Stat(key, a)], []), Sheet([Stat(key, b)], []));
        Assert.Equal(better, diff.Stats.Single().BBetter);
    }

    [Fact]
    public void ATextStatHasNoBetterSide() =>
        Assert.Null(SheetDiff.Of(Sheet([Stat("style", null)], []), Sheet([Stat("style", null)], [])).Stats[0].BBetter);

    [Fact]
    public void ATextStatDiffersWhenItsTextDoes()
    {
        var a = Sheet([new StatLine("stance", StatGroup.Offence, Note.Of("Attack Stance"), null, "Slash")], []);
        var b = Sheet([new StatLine("stance", StatGroup.Offence, Note.Of("Attack Stance"), null, "Stab")], []);
        Assert.True(SheetDiff.Of(a, b).Stats.Single().Differs);
    }

    [Theory]
    [InlineData("2.7s", "4s", "+1.3s")]
    [InlineData("6,141", "8,970", "+2,829")]
    [InlineData("150%", "0%", "-150%")]
    [InlineData("18", "0", "-18")]
    [InlineData("-8", "-8", "")]
    [InlineData("2.70s", "2.65s", "-0.05s")]
    public void TheDifferenceIsWrittenAsTheStatIs(string a, string b, string expected) =>
        Assert.Equal(expected, SheetDiff.TextDelta(a, b));

    [Theory]
    [InlineData("Melee", "Ranged")]
    [InlineData("5%", "7s")]
    [InlineData("12", "")]
    public void TextsThatDontLineUpHaveNoWrittenDifference(string a, string b) =>
        Assert.Null(SheetDiff.TextDelta(a, b));

    [Fact]
    public void StatsOnOneSideOnlyAreKept()
    {
        var a = Sheet([Stat("accuracy", 10), Stat("power:Normal", 5)], []);
        var b = Sheet([Stat("accuracy", 10), Stat("power:Fire", 7)], []);
        var diff = SheetDiff.Of(a, b);
        Assert.Equal(["accuracy", "power:Normal", "power:Fire"], diff.Stats.Select(s => s.Key));
        Assert.Null(diff.Stats[1].B);
        Assert.Null(diff.Stats[2].A);
        Assert.True(diff.Stats[1].Differs);
        Assert.True(diff.Stats[2].Differs);
        Assert.Null(diff.Stats[2].Delta);
    }

    [Fact]
    public void ModifiersOnOneSideOnlyAreMarked()
    {
        var shared = Mod("Iron_Helmet", "+3 Stab Defence Bonus");
        var a = Sheet([], [shared, Mod("Stone_Sword", "+12 Stab Accuracy Bonus")]);
        var b = Sheet(
            [],
            [
                shared,
                Mod("Adamant_Sword", "+40 Stab Accuracy Bonus"),
                Mod("Tough_Skin", "+5% Melee Defence Bonus", ModifierGroup.Prayer),
            ]
        );
        var sides = SheetDiff.Of(a, b).Modifiers;
        Assert.Equal(4, sides.Count);
        Assert.Equal((true, true), (sides[0].InA, sides[0].InB));
        Assert.Equal("Stone Sword", sides[1].Line.SourceName);
        Assert.Equal((true, false), (sides[1].InA, sides[1].InB));
        Assert.Equal((false, true), (sides[2].InA, sides[2].InB));
        Assert.Equal((false, true), (sides[3].InA, sides[3].InB));
        Assert.True(sides[1].OneSide);
        Assert.False(sides[0].OneSide);
    }

    [Fact]
    public void AModifierMatchesOnlyWithTheSameGroupSourceAndDescription()
    {
        // The same item as equipment on A and as food on B is two different lines.
        var a = Sheet([], [Mod("Shrimp", "+1 HP", ModifierGroup.Equipment)]);
        var b = Sheet([], [Mod("Shrimp", "+1 HP", ModifierGroup.Food)]);
        var sides = SheetDiff.Of(a, b).Modifiers;
        Assert.Equal(2, sides.Count);
        Assert.All(sides, s => Assert.True(s.OneSide));
        // A changed value is a change of description.
        var c = Sheet([], [Mod("Shrimp", "+2 HP", ModifierGroup.Equipment)]);
        Assert.All(SheetDiff.Of(a, c).Modifiers, s => Assert.True(s.OneSide));
    }

    [Fact]
    public void TheSameLineTwiceMatchesOneForOne()
    {
        var line = Mod("Ring", "+1% Lifesteal");
        var a = Sheet([], [line, line]);
        var b = Sheet([], [line]);
        var sides = SheetDiff.Of(a, b).Modifiers;
        Assert.Equal(2, sides.Count);
        Assert.Single(sides, s => s.InA && s.InB);
        Assert.Single(sides, s => s.InA && !s.InB);
    }
}
