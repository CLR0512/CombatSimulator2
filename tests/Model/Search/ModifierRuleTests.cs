using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ModifierRuleTests
{
    private static string? Opaque(
        string key,
        bool known = false,
        Direction direction = Direction.Good,
        bool conditional = false,
        bool scaling = false,
        bool onEnemy = false
    ) => ModifierRule.Opaque(key, known, direction, conditional, scaling, onEnemy);

    [Fact]
    public void AnUnknownModifierTheGameLabelsIsCompared()
    {
        Assert.Null(Opaque("New:Character.Shiny"));
        Assert.Null(Opaque("New:Character.Rust", direction: Direction.Bad));
    }

    [Fact]
    public void AnUnknownModifierLabelledNeitherStaysOpaque() =>
        Assert.Equal("modifier New:Character.Odd", Opaque("New:Character.Odd", direction: Direction.Neutral));

    [Fact]
    public void AMisleadingLabelStaysOpaque()
    {
        foreach (var key in ModifierRule.Misleading)
            Assert.Equal("modifier " + key, Opaque(key));
    }

    [Fact]
    public void AConditionIsComparedAsAStatOfItsOwn()
    {
        Assert.Null(Opaque("New:Character.Shiny", conditional: true));
        Assert.Null(Opaque("MelvorBase:Experience.PercentXPGained", known: true, conditional: true));
        Assert.Equal("if FightingCondition Goblin", ModifierRule.Context(null, "FightingCondition Goblin"));
        Assert.Equal("+#% Damage if Night", ModifierRule.Context("+#% Damage", "Night"));
        Assert.Equal("+#% Damage", ModifierRule.Context("+#% Damage", null));
        Assert.NotEqual(ModifierRule.Context("+#%", "Night"), ModifierRule.Context("+#%", "Day"));
    }

    [Fact]
    public void ScalingAndEnemyModifiersStayOpaque()
    {
        Assert.Equal("scaling modifier New:Character.Shiny", Opaque("New:Character.Shiny", scaling: true));
        Assert.Equal(
            "modifier on the enemy MelvorBase:Character.FlatMaxHit",
            Opaque("MelvorBase:Character.FlatMaxHit", known: true, onEnemy: true)
        );
    }

    [Fact]
    public void AnItemWithMoreOfAnUnknownGoodModifierBeatsOneWithLess()
    {
        // Two amulets alike but for a new modifier the game calls good: the one with more of it rules the other out,
        // whatever the goal; one with the modifier under a condition compares only with its own condition.
        PartModifier Shiny(double value, string? context = null) =>
            new("New:Character.Shiny", context, value, Direction.Good, PartGroup.Other);
        var catalogue = Catalogue(i =>
            i.Name switch
            {
                "AmuletOfAccuracy" => i with { Bonuses = [Shiny(10)] },
                "AmuletOfLearning" => i with { Bonuses = [Shiny(5)] },
                "OddAmulet" => i with { Opaque = false, OpaqueReason = null, Bonuses = [Shiny(20, "if Night")] },
                _ => i,
            }
        );
        foreach (var metric in new[] { "kills", "xp:MelvorBase:Attack" })
        {
            var measure = new Measure(catalogue, new Goal(metric));
            Assert.True(measure.Beats(Id("AmuletOfAccuracy"), Id("AmuletOfLearning")));
            Assert.False(measure.Beats(Id("AmuletOfLearning"), Id("AmuletOfAccuracy")));
            // More under a condition is not more always, nor the reverse.
            Assert.False(measure.Beats(Id("OddAmulet"), Id("AmuletOfAccuracy")));
            Assert.False(measure.Beats(Id("AmuletOfAccuracy"), Id("OddAmulet")));
        }
    }
}
