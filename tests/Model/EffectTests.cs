using MelvorMods.CombatSimulator2.Model;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SheetDiffTests;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class EffectTests
{
    [Fact]
    public void OnlyWhatDiffersIsListed()
    {
        var other = Sheet(
            [Stat("accuracy", 4812), Stat("max-hit", 151), Stat("style", null)],
            [Mod("Iron_Sword", "+5 Strength"), Mod("Thick_Skin", "+5% Defence", ModifierGroup.Prayer)]
        );
        var side = Sheet(
            [Stat("accuracy", 5100), Stat("max-hit", 151), Stat("style", null)],
            [Mod("Steel_Sword", "+9 Strength"), Mod("Thick_Skin", "+5% Defence", ModifierGroup.Prayer)]
        );
        var effect = Effect.Of(other, side);
        var stat = Assert.Single(effect.Stats);
        Assert.Equal("accuracy", stat.Key);
        Assert.Equal(4812, stat.A!.Value);
        Assert.Equal(5100, stat.B!.Value);
        Assert.Equal("+288", Effect.Difference(stat));
        Assert.Equal(3, effect.AllStats);
        Assert.Equal(["Iron Sword", "Steel Sword"], effect.Modifiers.Select(m => m.Line.SourceName));
        Assert.Equal(1, effect.Added);
        Assert.Equal(1, effect.Removed);
        Assert.False(effect.IsEmpty);
    }

    [Fact]
    public void LinesThatReadAlikeAreMergedWithTheirCount()
    {
        var other = Sheet([Stat("accuracy", 1)], []);
        var side = Sheet(
            [Stat("accuracy", 1)],
            [
                Mod("Climbing", "+20% chance to repeat a failed obstacle"),
                Mod("Iron_Sword", "+5 Strength"),
                Mod("Climbing", "+20% chance to repeat a failed obstacle"),
                Mod("Climbing", "+20% chance to repeat a failed obstacle"),
            ]
        );
        var effect = Effect.Of(other, side);
        Assert.Equal(4, effect.Added);
        Assert.Equal(
            [("Climbing", 3), ("Iron Sword", 1)],
            effect.Merged.Select(m => (m.Side.Line.SourceName, m.Count))
        );
    }

    [Fact]
    public void EqualSheetsHaveNoEffect()
    {
        var sheet = Sheet([Stat("accuracy", 4812)], [Mod("Iron_Sword", "+5 Strength")]);
        var effect = Effect.Of(sheet, sheet);
        Assert.True(effect.IsEmpty);
        Assert.Equal(1, effect.AllStats);
    }

    [Fact]
    public void AStatOnOneSheetOrANameHasNoWrittenDifference()
    {
        var other = Sheet([new StatLine("stance", StatGroup.Offence, Note.Of("Attack Stance"), null, "Slash")], []);
        var side = Sheet(
            [new StatLine("stance", StatGroup.Offence, Note.Of("Attack Stance"), null, "Stab"), Stat("power:fire", 12)],
            []
        );
        var effect = Effect.Of(other, side);
        Assert.Equal(["stance", "power:fire"], effect.Stats.Select(s => s.Key));
        Assert.All(effect.Stats, s => Assert.Null(Effect.Difference(s)));
    }
}
