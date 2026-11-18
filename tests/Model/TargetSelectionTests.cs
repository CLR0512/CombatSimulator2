using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class TargetSelectionTests
{
    private static readonly TargetFact[] Catalog =
    [
        new("Forest/Golbin", "Golbin", "Forest", "Golbin Forest", TargetTags.Monsters, true),
        new("Forest/Ranger", "Golbin Ranger", "Forest", "Golbin Forest", TargetTags.Monsters, false),
        new("Crypt/Wraith", "Wraith", "Crypt", "Slayer Crypt", TargetTags.Monsters | TargetTags.Slayer, true),
        new("Camp", "Hobgolbin Camp", "Camp", "Hobgolbin Camp", TargetTags.Dungeons, true),
        new("Peak/Dragon", "Dragon", "Peak", "Dragon Peak", TargetTags.Monsters, false),
    ];

    private static readonly HashSet<string> None = [];

    private static string[] Keys(IEnumerable<TargetFact> targets) => [.. targets.Select(t => t.Key)];

    [Fact]
    public void CanFightShowsWhatTheSetupCanFightInTheCataloguesOrder()
    {
        var selection = TargetSelection.Of(Catalog, new TargetFilter(), None);
        Assert.Equal(["Forest/Golbin", "Crypt/Wraith", "Camp"], Keys(selection.Shown));
        Assert.Equal(Keys(selection.Shown), Keys(selection.ToRun));
        Assert.Empty(selection.Ignored);
    }

    [Fact]
    public void QuotedSearchMatchesAWholeName()
    {
        var all = new TargetFilter { CanFightOnly = false };
        Assert.Equal(
            ["Forest/Golbin"],
            Keys(TargetSelection.Of(Catalog, all with { Search = "\"golbin\"" }, None).Shown)
        );
        Assert.Equal(
            ["Forest/Golbin", "Forest/Ranger", "Camp"],
            Keys(TargetSelection.Of(Catalog, all with { Search = "golbin" }, None).Shown)
        );
        Assert.Equal(
            ["Forest/Golbin"],
            Keys(TargetSelection.Of(Catalog, all with { Search = "\"Golbin Forest · Golbin\"" }, None).Shown)
        );
    }

    [Fact]
    public void ExactSearchNamesTheAreaOnlyWhenTheNameIsShared()
    {
        TargetFact[] twice = [.. Catalog, Catalog[0] with { Key = "Cave/Golbin", Area = "Cave", AreaName = "Cave" }];
        Assert.Equal("\"Wraith\"", TargetSelection.ExactSearch(twice, Catalog[2]));
        Assert.Equal("\"Golbin Forest · Golbin\"", TargetSelection.ExactSearch(twice, Catalog[0]));
    }

    [Fact]
    public void AllKeepsLockedTargetsInTheRun()
    {
        var selection = TargetSelection.Of(Catalog, new TargetFilter { CanFightOnly = false }, None);
        Assert.Equal(Keys(Catalog), Keys(selection.Shown));
        Assert.Equal(Keys(Catalog), Keys(selection.ToRun));
    }

    [Fact]
    public void IgnoredTargetsAreNeverRun()
    {
        var ignored = new HashSet<string> { "Camp", "Forest/Ranger" };
        var selection = TargetSelection.Of(Catalog, new TargetFilter(), ignored);
        Assert.Equal(["Forest/Golbin", "Crypt/Wraith", "Camp"], Keys(selection.Shown));
        Assert.Equal(["Camp"], Keys(selection.Ignored));
        Assert.Equal(["Forest/Golbin", "Crypt/Wraith"], Keys(selection.ToRun));
        var all = TargetSelection.Of(Catalog, new TargetFilter { CanFightOnly = false }, ignored);
        Assert.Equal(["Forest/Ranger", "Camp"], Keys(all.Ignored));
        Assert.DoesNotContain(all.ToRun, t => ignored.Contains(t.Key));
    }

    [Fact]
    public void AnIgnoreOutsideTheFilterStays()
    {
        var ignored = new HashSet<string> { "Camp" };
        var selection = TargetSelection.Of(Catalog, new TargetFilter { Kinds = TargetTags.Monsters }, ignored);
        Assert.Empty(selection.Ignored);
        Assert.Contains("Camp", ignored);
        Assert.Equal(["Camp"], Keys(TargetSelection.Of(Catalog, new TargetFilter(), ignored).Ignored));
    }

    [Fact]
    public void ATargetShowsWhenAtLeastOneOfItsTagsIsOn()
    {
        string[] With(TargetTags kinds) =>
            Keys(TargetSelection.Of(Catalog, new TargetFilter { Kinds = kinds }, None).Shown);
        // Slayer alone: the monster in a slayer area shows by its slayer tag.
        Assert.Equal(["Crypt/Wraith"], With(TargetTags.Slayer));
        // Monsters alone: the same monster shows by its monster tag.
        Assert.Equal(["Forest/Golbin", "Crypt/Wraith"], With(TargetTags.Monsters));
        // All three off: nothing.
        Assert.Empty(With(TargetTags.None));
        Assert.Equal(["Camp"], With(TargetTags.Dungeons));
        Assert.Equal(["Crypt/Wraith", "Camp"], With(TargetTags.Dungeons | TargetTags.Slayer));
    }

    [Fact]
    public void AreasAndSearchNarrow()
    {
        var byArea = TargetSelection.Of(Catalog, new TargetFilter { CanFightOnly = false, Areas = ["Forest"] }, None);
        Assert.Equal(["Forest/Golbin", "Forest/Ranger"], Keys(byArea.Shown));
        var byName = TargetSelection.Of(Catalog, new TargetFilter { CanFightOnly = false, Search = " golbin " }, None);
        Assert.Equal(["Forest/Golbin", "Forest/Ranger", "Camp"], Keys(byName.Shown));
        var byAreaName = TargetSelection.Of(Catalog, new TargetFilter { Search = "crypt" }, None);
        Assert.Equal(["Crypt/Wraith"], Keys(byAreaName.Shown));
        Assert.Empty(TargetSelection.Of(Catalog, new TargetFilter { Areas = [] }, None).Shown);
    }

    [Fact]
    public void TheSearchIsNotPartOfASavedFilter()
    {
        var filter = new TargetFilter
        {
            Search = "golbin",
            Kinds = TargetTags.Dungeons,
            Areas = ["Forest"],
        };
        var read = System.Text.Json.JsonSerializer.Deserialize<TargetFilter>(
            System.Text.Json.JsonSerializer.Serialize(filter)
        );
        Assert.Equal(filter with { Search = "" }, read);
    }

    [Fact]
    public void MastersHaveTheirOwnKindAndShowByDefault()
    {
        TargetFact kuren = new("master:Kuren", "Kuren", "master:Kuren", "Kuren", TargetTags.Masters, true);
        var catalog = Catalog.Append(kuren).ToList();
        Assert.Contains("master:Kuren", Keys(TargetSelection.Of(catalog, new TargetFilter(), None).ToRun));
        var slayerOnly = new TargetFilter { Kinds = TargetTags.Slayer };
        Assert.Equal(["Crypt/Wraith"], Keys(TargetSelection.Of(catalog, slayerOnly, None).Shown));
        var mastersOnly = new TargetFilter { Kinds = TargetTags.Masters };
        Assert.Equal(["master:Kuren"], Keys(TargetSelection.Of(catalog, mastersOnly, None).Shown));
    }
}
