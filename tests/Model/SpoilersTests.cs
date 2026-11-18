using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SpoilersTests
{
    private static Spoilers Guard(params SpoilerEnemy[] enemies) =>
        Spoilers.Of(
            [
                new SpoilerPlace("Farm", "Farm", Open: true, ["Chicken", "Cow"]),
                new SpoilerPlace("Cave", "Dark Cave", Open: false, ["Bat", "Golem"]),
                new SpoilerPlace("Crypt", "Crypt", Open: false, ["Ghost", "Chicken"]),
            ],
            enemies
        );

    private static readonly SpoilerEnemy[] NoneKilled =
    [
        new("Chicken", "Chicken", false),
        new("Cow", "Cow", false),
        new("Bat", "Bat", false),
        new("Golem", "Golem", false),
        new("Ghost", "Ghost", false),
    ];

    [Fact]
    public void MonsterInOpenPlaceIsShownThoughNeverKilled()
    {
        var guard = Guard(NoneKilled);
        Assert.False(guard.Hides("Cow"));
        // Also in a locked place: meeting it in an open one is enough.
        Assert.False(guard.Hides("Chicken"));
    }

    [Fact]
    public void MonsterOnlyInLockedPlacesIsHiddenUntilKilled()
    {
        Assert.True(Guard(NoneKilled).Hides("Bat"));
        var killed = NoneKilled.Select(e => e.Id == "Bat" ? e with { Killed = true } : e).ToArray();
        Assert.False(Guard(killed).Hides("Bat"));
        Assert.Equal("Bat", Guard(killed).Name("Bat"));
    }

    [Fact]
    public void LockedPlaceIsHiddenOpenOneShown()
    {
        var guard = Guard(NoneKilled);
        Assert.True(guard.Hides("Cave"));
        Assert.False(guard.Hides("Farm"));
        Assert.Equal(5, guard.Count); // Cave, Crypt; Bat, Golem, Ghost
    }

    [Fact]
    public void PlaceFoughtInStaysShownThoughLocked()
    {
        var killed = NoneKilled.Select(e => e.Id == "Ghost" ? e with { Killed = true } : e).ToArray();
        var guard = Guard(killed);
        Assert.False(guard.Hides("Crypt"));
        Assert.True(guard.Hides("Cave"));
    }

    [Fact]
    public void NamesMaskEachHiddenPart()
    {
        var guard = Guard(NoneKilled);
        Assert.Equal(Spoilers.Mask, guard.Name("Dark Cave"));
        Assert.Equal("??? (???)", guard.Name("Bat (Dark Cave)"));
        Assert.Equal("Chicken (???)", guard.Name("Chicken (Crypt)"));
        Assert.Equal("Cow (Farm)", guard.Name("Cow (Farm)"));
        Assert.Equal("Raw Bat Wing", guard.Name("Raw Bat Wing"));
        Assert.Equal(12, guard.Value(12));
    }

    [Fact]
    public void NameSharedWithSomethingShownStays()
    {
        var guard = Spoilers.Of(
            [new SpoilerPlace("Farm", "Farm", true, ["Rat"]), new SpoilerPlace("Sewer", "Sewer", false, ["BigRat"])],
            [new SpoilerEnemy("Rat", "Rat", false), new SpoilerEnemy("BigRat", "Rat", false)]
        );
        Assert.True(guard.Hides("BigRat"));
        Assert.Equal("Rat", guard.Name("Rat"));
    }

    [Fact]
    public void OffHidesNothing()
    {
        Assert.Equal(0, Spoilers.None.Count);
        Assert.Equal("Bat (Dark Cave)", Spoilers.None.Name("Bat (Dark Cave)"));
        Assert.True(Spoilers.None.SameAs(Spoilers.None));
        Assert.False(Guard(NoneKilled).SameAs(Spoilers.None));
    }

    [Fact]
    public void PetsNotFoundAreHiddenAndChangeWhatIsHidden()
    {
        var places = new[] { new SpoilerPlace("Farm", "Farm", Open: true, ["Chicken"]) };
        var enemies = new[] { new SpoilerEnemy("Chicken", "Chicken", Killed: false) };
        var guard = Spoilers.Of(
            places,
            enemies,
            [new SpoilerPet("Bruno", Found: false), new SpoilerPet("Fen", Found: true)]
        );
        Assert.True(guard.PetHidden("Bruno"));
        Assert.False(guard.PetHidden("Fen"));
        Assert.False(guard.Hides("Bruno"));
        Assert.False(Spoilers.None.PetHidden("Bruno"));
        Assert.False(guard.SameAs(Spoilers.Of(places, enemies, [new SpoilerPet("Bruno", Found: true)])));
        Assert.True(guard.SameAs(Spoilers.Of(places, enemies, [new SpoilerPet("Bruno", Found: false)])));
    }
}
