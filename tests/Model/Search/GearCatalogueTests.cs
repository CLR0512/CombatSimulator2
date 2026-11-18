using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class GearCatalogueTests
{
    [Fact]
    public void TheFixtureHasEveryKindOfItem()
    {
        var catalogue = Catalogue();
        Assert.Equal(12, catalogue.Slots.Count);
        Assert.InRange(catalogue.Items.Count, 35, 45);
        Assert.Contains(catalogue.Items, i => i.Occupies.Count > 0);
        Assert.Contains(catalogue.Items, i => i.AmmoType != null);
        Assert.Contains(catalogue.Items, i => i.Effects.Count > 0);
        Assert.Contains(catalogue.Items, i => i.Opaque);
        Assert.Contains(catalogue.Items, i => i.Owned == 0);
        Assert.Contains(catalogue.Items, i => !i.Wearable);
        Assert.Equal(4, catalogue.Items.Count(i => i.Sets.Count > 0));
        Assert.Equal(6, catalogue.Current.Count);
    }

    [Fact]
    public void TheHashIsStable()
    {
        Assert.Equal(Catalogue().Hash, Catalogue().Hash);
        Assert.Equal(12, Catalogue().Hash.Length);
    }

    [Fact]
    public void TheHashChangesWithAnOwnedCountASetCouldUse()
    {
        // A second Silver Ring can go in the second ring slot.
        var more = Catalogue(i => i.Id == Id("SilverRing") ? i with { Owned = 2 } : i);
        Assert.NotEqual(Catalogue().GearHash, more.GearHash);
        Assert.Equal(Catalogue().LevelsHash, more.LevelsHash);
        Assert.NotEqual(Catalogue().Hash, more.Hash);
        var none = Catalogue(i => i.Id == Id("SilverRing") ? i with { Owned = 0 } : i);
        Assert.NotEqual(Catalogue().GearHash, none.GearHash);
    }

    [Fact]
    public void CountsNoSetCouldUseLeaveTheHash()
    {
        // Arrows used up in play, and a second sword for the one weapon slot.
        var fewer = Catalogue(i => i.Id == Id("IronArrows") ? i with { Owned = 37 } : i);
        Assert.Equal(Catalogue().GearHash, fewer.GearHash);
        var second = Catalogue(i => i.Id == Id("IronSword") ? i with { Owned = 2 } : i);
        Assert.Equal(Catalogue().GearHash, second.GearHash);
    }

    [Fact]
    public void TheHashChangesWithWhatIsWorn()
    {
        var other = Catalogue() with { Current = new Dictionary<string, string> { [Id("Weapon")] = Id("IronSword") } };
        Assert.NotEqual(Catalogue().GearHash, other.GearHash);
    }

    [Fact]
    public void TheHashChangesWithALevelAndTheBuild()
    {
        var levelled = Catalogue() with { Levels = new Dictionary<string, int> { [Id("Attack")] = 41 } };
        Assert.Equal(Catalogue().GearHash, levelled.GearHash);
        Assert.NotEqual(Catalogue().LevelsHash, levelled.LevelsHash);
        Assert.NotEqual(Catalogue().Hash, levelled.Hash);
        Assert.NotEqual(Catalogue().Hash, (Catalogue() with { Build = "another" }).Hash);
    }

    [Fact]
    public void AnItemsDisplayNameIsNotInTheHash()
    {
        var renamed = Catalogue(i => i with { Name = i.Name + "!" });
        Assert.Equal(Catalogue().Hash, renamed.Hash);
    }
}
