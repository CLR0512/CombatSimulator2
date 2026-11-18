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
