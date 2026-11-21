using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SettlingTests
{
    // One option fought in the finished set: the same pace on the goal for all, so only the defensive measures differ.
    private static (SettleOption Winner, DecidedBy By, SettleOption? Against, Estimate? Difference) Decide(
        IReadOnlyList<SettleOption> options
    ) => Settling.Decide(options, Metrics.Kills);

    private static SettleOption Option(
        string item,
        int order,
        int deaths = 0,
        double damage = 0,
        long food = 0,
        bool worn = false,
        bool owned = true,
        int unmet = 0,
        double pace = 5
    )
    {
        var set = Taking(Set(order, pace, deaths: deaths), damage);
        foreach (var batch in set.Result.Batches)
            if (food > 0)
                batch.Used["food"] = food * batch.Trials;
        return new SettleOption(item, item, worn, owned, unmet, order, set.Result);
    }

    [Fact]
    public void TheChanceOfDyingDecidesFirst()
    {
        var (winner, by, against, difference) = Decide([
            // As many kills an hour: the deaths' trials are shorter.
            Option("Plate", 1, deaths: 30, damage: 1, pace: 4.5),
            Option("Robe", 2, deaths: 0, damage: 3),
        ]);
        Assert.Equal("Robe", winner.Item);
        Assert.Equal(DecidedBy.Deaths, by);
        Assert.Equal("Plate", against!.Item);
        Assert.Equal(-0.1, difference!.Point, 6);
        Assert.NotEqual("relative", difference.Method);
    }

    [Fact]
    public void TheGoalReadAtTheSettlingsLengthDecidesBeforeAnyDefence()
    {
        var (winner, by, against, _) = Decide([Option("Slow", 1, damage: 1, pace: 5.5), Option("Fast", 2, damage: 3)]);
        Assert.Equal("Fast", winner.Item);
        Assert.Equal(DecidedBy.Goal, by);
        Assert.Equal("Slow", against!.Item);
    }

    [Fact]
    public void ForTheClosestSetDeathsDecideBeforeTheGoal()
    {
        // Faster kills against fewer deaths: the goal picks the first, the closest set's settling the second.
        var options = new[] { Option("Fast", 1, deaths: 30, pace: 2.5), Option("Safe", 2, deaths: 0, pace: 5) };
        Assert.Equal("Fast", Settling.Decide(options, Metrics.Kills).Winner.Item);
        var (winner, by, _, _) = Settling.Decide(options, Metrics.Kills, deathsFirst: true);
        Assert.Equal("Safe", winner.Item);
        Assert.Equal(DecidedBy.Deaths, by);
    }

    [Fact]
    public void DamageTakenDecidesWhenDeathsDont()
    {
        var (winner, by, against, difference) = Decide([
            Option("Leather", 1, damage: 2),
            Option("Steel", 2, damage: 1),
        ]);
        Assert.Equal("Steel", winner.Item);
        Assert.Equal(DecidedBy.DamageTaken, by);
        Assert.Equal("Leather", against!.Item);
        Assert.True(difference!.Point < 0);
        Assert.Equal(-0.5, difference.Point, 6);
    }

    [Fact]
    public void FoodDecidesWhenDamageDoesnt()
    {
        var (winner, by, _, _) = Decide([Option("A", 1, damage: 1, food: 10), Option("B", 2, damage: 1, food: 4)]);
        Assert.Equal("B", winner.Item);
        Assert.Equal(DecidedBy.Food, by);
    }

    [Fact]
    public void OptionsClearlyWorseDropOutAndTheRestGoOn()
    {
        // C takes the most damage and drops out; A and B take the same, and food tells them apart.
        var (winner, by, against, _) = Decide([
            Option("A", 1, damage: 1, food: 9),
            Option("B", 2, damage: 1, food: 3),
            Option("C", 3, damage: 4, food: 1),
        ]);
        Assert.Equal("B", winner.Item);
        Assert.Equal(DecidedBy.Food, by);
        Assert.Equal("A", against!.Item);
    }

    [Fact]
    public void AMeasureThatRulesSomeOutNamesItselfThoughTheTieOrderPicksAmongTheRest()
    {
        // The worn cape takes more damage than the two others, which nothing then tells apart: the first in the
        // game's order is kept, and damage taken decided against the worn one.
        var (winner, by, against, _) = Decide([
            Option("Worn", 3, damage: 3, worn: true),
            Option("Second", 2, damage: 1),
            Option("First", 1, damage: 1),
        ]);
        Assert.Equal("First", winner.Item);
        Assert.Equal(DecidedBy.DamageTaken, by);
        Assert.Equal("Worn", against!.Item);
    }

    [Fact]
    public void WhenNothingSeparatesThemTheTieOrderPicks()
    {
        Assert.Equal("Worn", Decide([Option("Owned", 1), Option("Worn", 2, worn: true)]).Winner.Item);
        Assert.Equal("Owned", Decide([Option("Unowned", 1, owned: false), Option("Owned", 2)]).Winner.Item);
        Assert.Equal(
            "Fewer",
            Decide([Option("More", 1, owned: false, unmet: 2), Option("Fewer", 2, owned: false, unmet: 1)]).Winner.Item
        );
        Assert.Equal("Held", Decide([Option("Other", 1), Option("Held", 2) with { Held = true }]).Winner.Item);
        var first = Decide([Option("Second", 2), Option("First", 1)]);
        Assert.Equal("First", first.Winner.Item);
        Assert.Equal(DecidedBy.Nothing, first.By);
        Assert.Null(first.Against);
    }

    [Fact]
    public void ATargetThatCantHurtTheCharacterIsSettledByTheTieOrder()
    {
        var (winner, by, _, _) = Decide([Option("Cape", 2), Option("None", 1, worn: true)]);
        Assert.Equal("None", winner.Item);
        Assert.Equal(DecidedBy.Nothing, by);
    }

    [Fact]
    public void OneOptionIsSettledByNothing() => Assert.Equal(DecidedBy.Nothing, Decide([Option("Only", 1)]).By);
}
