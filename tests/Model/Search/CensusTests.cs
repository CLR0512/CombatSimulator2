using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class CensusTests
{
    private static GearCatalogue WithUnknowns() =>
        Catalogue(i =>
            i.Name switch
            {
                "IronHelmet" => i with { Unknowns = ["modifier Avoid"] },
                "SteelHelmet" => i with { Unknowns = ["modifier Avoid"], Owned = 0 },
                "IronBoots" => i with { Unknowns = ["modifier Avoid", "effect kind Odd"] },
                "PlainCape" => i with { Unknowns = ["effect kind Odd"] },
                "IronGloves" => i with { Unknowns = ["stat Luck"] },
                _ => i,
            }
        ) with
        {
            SetBonuses =
            [
                new GearSetBonus(Id("Odd"), "Odd set", [], new Dictionary<int, IReadOnlyList<PartModifier>>())
                {
                    Unknowns = ["stat Luck", "modifier Strange"],
                },
            ],
        };

    [Fact]
    public void EachPartCountsTheItemsCarryingItAndThoseItAloneHoldsBack()
    {
        var rows = Census.Of(WithUnknowns()).ToDictionary(r => r.Part);
        var avoid = rows["modifier Avoid"];
        // Three items carry it, one of them not owned; it alone holds back the two helmets, so they are named.
        Assert.Equal((3, 2, 2, 0), (avoid.Items, avoid.Owned, avoid.Alone, avoid.SetBonuses));
        Assert.Equal(["IronHelmet", "SteelHelmet"], avoid.Names);
        Assert.Equal(
            (2, 1, 0),
            (rows["effect kind Odd"].Items, rows["effect kind Odd"].Alone, rows["effect kind Odd"].SetBonuses)
        );
        Assert.Equal(1, rows["stat Luck"].SetBonuses);
        // A part only a set bonus carries is still listed.
        Assert.Equal((0, 1), (rows["modifier Strange"].Items, rows["modifier Strange"].SetBonuses));
    }

    [Fact]
    public void ThePartThatWouldFreeTheMostItemsComesFirst()
    {
        var rows = Census.Of(WithUnknowns());
        Assert.Equal(["modifier Avoid", "effect kind Odd", "stat Luck", "modifier Strange"], rows.Select(r => r.Part));
    }

    [Fact]
    public void TheTotalsCountEveryItemTheSearchCannotCompare()
    {
        // Five items with unknown parts, and the fixture's Odd Amulet, opaque without one named.
        Assert.Equal((6, Catalogue().Items.Count), Census.Totals(WithUnknowns()));
    }
}
