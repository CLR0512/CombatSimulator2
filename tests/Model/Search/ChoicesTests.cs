using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ChoicesTests
{
    private static ChoiceOption Prayer(string id, IReadOnlyList<string>? current = null) =>
        new(ChoiceKind.Prayers, [id], Choices.PrayerChanges(current ?? [], [id]));

    private static Choice Prayers(int slots, params string[] current) =>
        new(
            ChoiceKind.Prayers,
            null,
            new ChoiceOption(ChoiceKind.Prayers, current, ChangeList.Empty),
            [.. "ABCDEF".Select(c => Prayer(c.ToString(), current))],
            slots
        );

    private static ChoiceOption Spell(string id, string element) =>
        new(ChoiceKind.Spell, [id], ChangeList.Of([new Change(ChangeKind.Spell, null, id)]))
        {
            Needs = [element],
            NeedsMagic = true,
        };

    [Fact]
    public void AnOptionTheCharacterCanUseIsACandidate() => Assert.True(Prayer("A").IsCandidate(false));

    [Fact]
    public void AnOptionActingOnlyOutsideCombatIsNeverACandidate() =>
        Assert.False((Prayer("A") with { OutsideCombat = true }).IsCandidate(true));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void UnownedOrLockedOptionsOnlyWhenIncluded(bool include, bool candidate)
    {
        var unowned = Prayer("A") with { Owned = false };
        var locked = Prayer("B") with { Unmet = [Note.Of("Prayer level 40")] };
        Assert.Equal(candidate, unowned.IsCandidate(include));
        Assert.Equal(candidate, locked.IsCandidate(include));
    }

    [Fact]
    public void ASpellNeedsItsElementAndAMagicStance()
    {
        var air = Spell("WindStrike", "Air");
        Assert.True(air.Allowed(["Air", "Water"], magicStance: true));
        Assert.False(air.Allowed(["Water"], magicStance: true));
        Assert.False(air.Allowed(["Air"], magicStance: false));
    }

    [Fact]
    public void AnImbuementNeedsOnlyItsElement()
    {
        var imbuement = Spell("AirImbue", "Air") with { Kind = ChoiceKind.Imbuement, NeedsMagic = false };
        Assert.True(imbuement.Allowed(["Air"], magicStance: false));
    }

    [Fact]
    public void ForLeavesOutTheSetupsOwnAndWhatGearDoesntAllow()
    {
        var current = Spell("WindStrike", "Air");
        var choice = new Choice(
            ChoiceKind.Spell,
            null,
            current,
            [current, Spell("WaterStrike", "Water"), Spell("FireStrike", "Fire")]
        );
        var tried = Choices.For(choice, ["Air", "Water"], magicStance: true, includeUnowned: false);
        Assert.Equal(["WaterStrike"], tried.Select(o => o.Id));
    }

    [Fact]
    public void PairsAreMadeFromTheBestFourOnly()
    {
        var choice = Prayers(2);
        var best = choice.Options.ToList();
        var pairs = Choices.Pairs(choice, best);
        Assert.Equal(6, pairs.Count);
        Assert.All(pairs, p => Assert.DoesNotContain(p.Ids, id => id is "E" or "F"));
        Assert.Equal(pairs.Count, pairs.Select(p => string.Join('+', p.Ids)).Distinct().Count());
    }

    [Fact]
    public void NoPairsWithOneSlot() => Assert.Empty(Choices.Pairs(Prayers(1), Prayers(1).Options));

    [Fact]
    public void ThePairTheSetupHasIsLeftOut()
    {
        var choice = Prayers(2, "B", "A");
        var pairs = Choices.Pairs(choice, choice.Options);
        Assert.Equal(5, pairs.Count);
        Assert.DoesNotContain(pairs, p => p.Ids.Order().SequenceEqual(["A", "B"]));
    }

    [Fact]
    public void APrayerIsNeverPairedWithItself()
    {
        var choice = Prayers(2);
        var a = choice.Options[0];
        var pairs = Choices.Pairs(choice, [a, a, choice.Options[1]]);
        Assert.Equal(["A", "B"], Assert.Single(pairs).Ids);
    }

    [Fact]
    public void APairTurnsOffWhatItReplacesAndOnWhatItAdds()
    {
        var choice = Prayers(2, "A", "C");
        var pair = Choices.Pairs(choice, [choice.Options[0], choice.Options[1]]).Single();
        Assert.Equal("prayer=B:on prayer=C:off", pair.Changes.ShortForm);
    }

    [Fact]
    public void APairCarriesBothSinglesUnmetRequirements()
    {
        var choice = Prayers(2);
        var a = choice.Options[0] with { Unmet = [Note.Of("Prayer level 40")] };
        var pair = Choices.Pairs(choice, [a, choice.Options[1]]).Single();
        Assert.Single(pair.Unmet);
    }

    [Fact]
    public void KindsHaveTheirChangeKindAndName()
    {
        Assert.Equal(ChangeKind.Prayer, ChoiceKinds.Change(ChoiceKind.Prayers));
        Assert.Equal(ChangeKind.Toolbelt, ChoiceKinds.Change(ChoiceKind.Toolbelt));
        Assert.True(ChoiceKinds.TryParse("imbuement", out var kind));
        Assert.Equal(ChoiceKind.Imbuement, kind);
        Assert.False(ChoiceKinds.TryParse("gear", out _));
    }

    [Fact]
    public void FoodIsOffByDefault()
    {
        Assert.DoesNotContain(ChoiceKind.Food, ChoiceKinds.Defaults);
        Assert.Contains(ChoiceKind.Prayers, ChoiceKinds.Defaults);
    }

    [Fact]
    public void AMagicWeaponFightsWithTheStrongestSpellItCanCast()
    {
        var spells = new Choice(
            ChoiceKind.Spell,
            null,
            new ChoiceOption(ChoiceKind.Spell, [], ChangeList.Empty),
            [
                Spell("WindStrike", "Air"),
                Spell("WaterStrike", "Water"),
                Spell("WindBolt", "Air"),
                Spell("FireBolt", "Fire") with
                {
                    Unmet = [Note.Of("Magic level {0}", 50)],
                },
            ]
        );
        Assert.Equal("WindBolt", Choices.SpellFor(spells, ["Air", "Water"], includeUnowned: false)!.Id);
        Assert.Equal("FireBolt", Choices.SpellFor(spells, ["Fire"], includeUnowned: true)!.Id);
        Assert.Null(Choices.SpellFor(spells, ["Fire"], includeUnowned: false));
    }
}
