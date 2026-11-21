using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SearchRequestTests
{
    internal static SearchRequest Request(SearchKind kind = SearchKind.BestGear, string metric = "kills") =>
        new(kind, new Setup { Character = "Dev" }, "MelvorBase:Farm/MelvorBase:Cow", new Goal(metric), 1000, 4);

    [Fact]
    public void APlainRequestIsFine() => Assert.Empty(Request().Check(Catalogue()));

    [Fact]
    public void LocksAndExclusionsThatExistAreFine()
    {
        var request = Request() with
        {
            Locks = new HashSet<string> { Id("Weapon") },
            Excluded = new HashSet<string> { Id("IronHelmet") },
        };
        Assert.Empty(request.Check(Catalogue()));
    }

    [Fact]
    public void ALockedSlotMustExist()
    {
        var request = Request() with { Locks = new HashSet<string> { Id("Tail") } };
        Assert.Equal(["No slot Tail"], request.Check(Catalogue()).Select(n => n.English()));
    }

    [Fact]
    public void AnExcludedIdMustBeAnItem()
    {
        var request = Request() with { Excluded = new HashSet<string> { Id("Cow") } };
        Assert.Equal(["Cow is not an item that can be worn"], request.Check(Catalogue()).Select(n => n.English()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("MelvorBase:Farm/MelvorBase:Cow MelvorBase:Farm/MelvorBase:Chicken")]
    public void TheTargetIsOne(string target)
    {
        var request = Request() with { Target = target };
        Assert.Equal(["Choose one target"], request.Check(Catalogue()).Select(n => n.English()));
    }

    [Fact]
    public void ARequestStartsOnTheOwnedPoolWithNothingNarrowed()
    {
        var request = Request();
        Assert.Equal(ItemPool.Owned, request.Pool);
        Assert.False(request.Narrowed);
    }

    [Fact]
    public void EveryItemWithItsNarrowingIsFineForOneTarget()
    {
        var request = Request() with
        {
            Pool = ItemPool.Every,
            WearableOnly = true,
            ReachedOnly = true,
            LeftOutSources = new HashSet<string> { "Shop" },
        };
        Assert.Empty(request.Check(Catalogue()));
    }

    [Theory]
    [InlineData("master:MelvorBase:Kuren")]
    [InlineData("area:MelvorBase:Farm")]
    [InlineData("pinned:MelvorBase:Farm/MelvorBase:Cow,MelvorBase:Farm/MelvorBase:Chicken")]
    public void EveryItemIsOneTargetAtATime(string target)
    {
        var request = Request() with { Pool = ItemPool.Every, Target = target };
        Assert.Equal(["Every item: one target at a time"], request.Check(Catalogue()).Select(n => n.English()));
        Assert.Equal(["Every item: one target at a time"], request.Refusals().Select(n => n.English()));
        Assert.Empty((request with { Pool = ItemPool.Owned }).Check(Catalogue()));
    }

    [Fact]
    public void NarrowingNeedsEveryItem()
    {
        foreach (
            var request in new[]
            {
                Request() with
                {
                    WearableOnly = true,
                },
                Request() with
                {
                    ReachedOnly = true,
                },
                Request() with
                {
                    LeftOutSources = new HashSet<string> { "Shop" },
                },
            }
        )
            Assert.Equal(["Narrowing the pool needs every item"], request.Check(Catalogue()).Select(n => n.English()));
    }

    [Fact]
    public void WhatNeedsTheCatalogueIsCheckedOnlyWithIt()
    {
        var request = Request() with { Locks = new HashSet<string> { Id("Tail") } };
        Assert.Empty(request.Refusals());
        Assert.Equal(["No slot Tail"], request.Check(Catalogue()).Select(n => n.English()));
    }

    [Fact]
    public void DroptimizerHasNoPoolToChoose() =>
        Assert.Equal(
            ["Droptimizer tries every item already"],
            (Request(SearchKind.Droptimizer) with { Pool = ItemPool.Every }).Check(Catalogue()).Select(n => n.English())
        );

    [Fact]
    public void DroptimizerTakesNoLocks()
    {
        var request = Request(SearchKind.Droptimizer) with { Locks = new HashSet<string> { Id("Weapon") } };
        Assert.Single(request.Check(Catalogue()));
    }

    [Fact]
    public void CurrentGearIsTheCandidateWithoutChanges()
    {
        Assert.True(new Candidate(0, ChangeList.Empty, "Melee").IsCurrent);
        Assert.False(new Candidate(1, ChangeList.Parse("equip=Weapon:IronSword"), "Melee").IsCurrent);
    }

    [Fact]
    public void ARequestWithoutKindsSearchesGearAlone()
    {
        Assert.Empty(Request().Kinds);
        Assert.False(Request().IncludeUnowned);
    }

    [Fact]
    public void KindsThatExistAreFine() =>
        Assert.Empty((Request() with { Kinds = ChoiceKinds.All, IncludeUnowned = true }).Check(Catalogue()));

    [Fact]
    public void AnUnknownKindIsRefused()
    {
        var request = Request() with { Kinds = [(ChoiceKind)42] };
        Assert.Equal(["No kind of choice 42"], request.Check(Catalogue()).Select(n => n.English()));
    }

    [Fact]
    public void DroptimizerTriesGearOnly() =>
        Assert.Single((Request(SearchKind.Droptimizer) with { Kinds = [ChoiceKind.Prayers] }).Check(Catalogue()));
}
