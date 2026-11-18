using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ChangeTests
{
    [Theory]
    [InlineData("level=Strength:60", ChangeKind.Level, "MelvorBase:Strength", "60")]
    [InlineData("equip=Weapon:Iron_Sword", ChangeKind.Equip, "MelvorBase:Weapon", "MelvorBase:Iron_Sword")]
    [InlineData("equip=Weapon:none", ChangeKind.Equip, "MelvorBase:Weapon", "none")]
    [InlineData("food=Shrimp", ChangeKind.Food, null, "MelvorBase:Shrimp")]
    [InlineData("food=none", ChangeKind.Food, null, "none")]
    [InlineData("prayer=Thick_Skin:on", ChangeKind.Prayer, "MelvorBase:Thick_Skin", "on")]
    [InlineData("prayer=Thick_Skin:off", ChangeKind.Prayer, "MelvorBase:Thick_Skin", "off")]
    [InlineData("potion=none", ChangeKind.Potion, null, "none")]
    [InlineData("stance=Stab", ChangeKind.Stance, null, "MelvorBase:Stab")]
    [InlineData("spell=AirBolt", ChangeKind.Spell, null, "MelvorBase:AirBolt")]
    [InlineData("imbuement=SuffocateI", ChangeKind.Imbuement, null, "MelvorBase:SuffocateI")]
    [InlineData("imbuement=none", ChangeKind.Imbuement, null, "none")]
    [InlineData("potion=Melee_Potion", ChangeKind.Potion, null, "MelvorBase:Melee_Potion")]
    public void ShortFormsRoundTrip(string text, ChangeKind kind, string? subject, string value)
    {
        var change = Change.Parse(text);
        Assert.Equal(kind, change.Kind);
        Assert.Equal(subject, change.Subject);
        Assert.Equal(value, change.Value);
        Assert.Equal(text, change.ShortForm);
        Assert.Equal(change, Change.Parse(change.ShortForm));
    }

    [Theory]
    [InlineData("equip=MelvorBase:Weapon:MelvorBase:Iron_Sword", "equip=Weapon:Iron_Sword")]
    [InlineData("equip=MelvorBase:Weapon:Iron_Sword", "equip=Weapon:Iron_Sword")]
    [InlineData("level=MelvorBase:Strength:60", "level=Strength:60")]
    [InlineData("prayer=MelvorBase:Thick_Skin:on", "prayer=Thick_Skin:on")]
    [InlineData("food=MelvorBase:Shrimp", "food=Shrimp")]
    [InlineData("equip=Weapon:Other:Sword", "equip=MelvorBase:Weapon:Other:Sword")]
    [InlineData("equip=Other:Slot:Other:Sword", "equip=Other:Slot:Other:Sword")]
    [InlineData("food=Other:Fish", "food=Other:Fish")]
    public void FullIdsAreShortenedAndOtherNamespacesKept(string text, string expected)
    {
        var change = Change.Parse(text);
        Assert.Equal(expected, change.ShortForm);
        Assert.Equal(change, Change.Parse(change.ShortForm));
    }

    [Fact]
    public void AnotherNamespaceNeverMakesTheShortFormAmbiguous()
    {
        var change = new Change(ChangeKind.Equip, "Other:Slot", "MelvorBase:Iron_Sword");
        Assert.Equal(change, Change.Parse(change.ShortForm));
    }

    [Theory]
    [InlineData("hat=Weapon:Iron_Sword")]
    [InlineData("Strength:60")]
    [InlineData("level=60")]
    [InlineData("level=:60")]
    [InlineData("equip=Iron_Sword")]
    [InlineData("prayer=on")]
    [InlineData("level=Strength:0")]
    [InlineData("level=Strength:1000")]
    [InlineData("level=Strength:sixty")]
    [InlineData("prayer=Thick_Skin:yes")]
    [InlineData("equip=Weapon:")]
    [InlineData("food=")]
    [InlineData("stance=")]
    [InlineData("stance=none")]
    [InlineData("spell=none")]
    [InlineData("spell=")]
    public void BadFormsAreErrors(string text)
    {
        Assert.Throws<FormatException>(() => Change.Parse(text));
        Assert.False(Change.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("level=Strength:1")]
    [InlineData("level=Strength:999")]
    public void LevelsFrom1To999AreWellFormed(string text) => Assert.Equal(text, Change.Parse(text).ShortForm);

    [Fact]
    public void ALaterEditReplacesTheEarlierOne()
    {
        var list = ChangeList
            .Empty.With(Change.Parse("level=Strength:50"))
            .With(Change.Parse("level=Strength:60"))
            .With(Change.Parse("level=Attack:40"));
        Assert.Equal(2, list.Count);
        Assert.Equal("level=Attack:40 level=Strength:60", list.ShortForm);
    }

    [Fact]
    public void CanonicalOrderIsKindThenSubject()
    {
        var list = ChangeList.Parse(
            "prayer=Thick_Skin:on potion=none imbuement=none spell=AirBolt stance=Stab food=Shrimp equip=Weapon:Iron_Sword equip=Helmet:none level=Strength:60 level=Attack:40"
        );
        Assert.Equal(
            "level=Attack:40 level=Strength:60 equip=Helmet:none equip=Weapon:Iron_Sword stance=Stab spell=AirBolt imbuement=none food=Shrimp potion=none prayer=Thick_Skin:on",
            list.ShortForm
        );
        Assert.Equal(
            [
                ChangeKind.Level,
                ChangeKind.Level,
                ChangeKind.Equip,
                ChangeKind.Equip,
                ChangeKind.Stance,
                ChangeKind.Spell,
                ChangeKind.Imbuement,
                ChangeKind.Food,
                ChangeKind.Potion,
                ChangeKind.Prayer,
            ],
            list.Select(c => c.Kind)
        );
    }

    [Fact]
    public void TheOrderChangesWereMadeInDoesNotMatter()
    {
        var a = ChangeList.Empty.With(Change.Parse("equip=Weapon:Iron_Sword")).With(Change.Parse("level=Strength:60"));
        var b = ChangeList.Empty.With(Change.Parse("level=Strength:60")).With(Change.Parse("equip=Weapon:Iron_Sword"));
        Assert.Equal(a.ShortForm, b.ShortForm);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void WithoutRemovesByKey()
    {
        var list = ChangeList.Parse("level=Strength:60 food=Shrimp");
        var without = list.Without(Change.Parse("food=none").Key);
        Assert.Equal("level=Strength:60", without.ShortForm);
        Assert.Same(list, list.Without(Change.Parse("level=Attack:1").Key));
    }

    [Fact]
    public void EmptyHasNoShortForm()
    {
        Assert.Equal("", ChangeList.Empty.ShortForm);
        Assert.True(ChangeList.Empty.IsEmpty);
        Assert.Equal(ChangeList.Empty, ChangeList.Parse(""));
        Assert.Equal(ChangeList.Empty, ChangeList.Parse("  "));
    }

    [Fact]
    public void ParseRoundTripsShort()
    {
        var list = ChangeList.Parse("food=none level=Hitpoints:60 prayer=Thick_Skin:off");
        Assert.Equal(list, ChangeList.Parse(list.ShortForm));
    }

    [Fact]
    public void ARepeatedKeyIsAnError() =>
        Assert.Throws<FormatException>(() => ChangeList.Parse("level=Strength:50 level=Strength:60"));

    [Fact]
    public void ABadChangeInAListIsAnError() =>
        Assert.Throws<FormatException>(() => ChangeList.Parse("level=Strength:60 hat=on"));

    [Fact]
    public void ACandidateIgnoresNoRequirementsUnlessGivenChanges()
    {
        var candidate = new Candidate(1, ChangeList.Parse("prayer=Thick_Skin:on food=Trout"), "Melee");
        Assert.True(candidate.Ignoring.IsEmpty);
        var ignoring = candidate with { Ignoring = ChangeList.Parse("prayer=Thick_Skin:on") };
        Assert.Equal([new ChangeKey(ChangeKind.Prayer, "MelvorBase:Thick_Skin")], ignoring.Ignoring.Select(c => c.Key));
    }
}
