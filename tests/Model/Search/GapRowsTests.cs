using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;
using static MelvorMods.CombatSimulator2.Tests.SearchFixture;
using static MelvorMods.CombatSimulator2.Tests.StageResults;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class GapRowsTests
{
    // The Guard body not owned, so the Guard set has a new piece.
    private static GearCatalogue Gear() => Catalogue(i => i.Id == Id("GuardBody") ? i with { Owned = 0 } : i);

    [Fact]
    public void AWeaponComesWithTheAnswersOffhandAndStance()
    {
        var set = ChangeList.Parse("equip=Weapon:RuneSword equip=Offhand:RuneShield stance=Stab");
        var swap = Assert.Single(GapRows.Swaps(Gear(), set));
        Assert.Equal([Id("RuneSword"), Id("RuneShield")], swap.Items);
        Assert.Equal(set, swap.Changes);
    }

    [Fact]
    public void ASetBonusPieceComesWithTheSetsOtherPiecesAndOwnedItemsHaveNoRow()
    {
        var set = ChangeList.Parse(
            "equip=Platebody:GuardBody equip=Helmet:GuardHelmet equip=Platelegs:IronPlatelegs equip=Boots:IronBoots"
        );
        var swap = Assert.Single(GapRows.Swaps(Gear(), set));
        Assert.Equal([Id("GuardBody")], swap.Items);
        Assert.Equal(ChangeList.Parse("equip=Platebody:GuardBody equip=Helmet:GuardHelmet"), swap.Changes);
    }

    [Fact]
    public void EachNewItemWithoutCompanyIsItsOwnSwap()
    {
        var swaps = GapRows.Swaps(Gear(), ChangeList.Parse("equip=Helmet:DragonHelmet equip=Offhand:RuneShield"));
        Assert.Equal(
            [
                [Id("RuneShield")],
                [Id("DragonHelmet")],
            ],
            swaps.Select(s => s.Items)
        );
    }

    [Fact]
    public void RowsGoLargestFirstWithTheirShareAndThoseWithinTheMarginSayItPaysOffOnlyWithTheRest()
    {
        var swaps = GapRows.Swaps(Gear(), ChangeList.Parse("equip=Helmet:DragonHelmet equip=Offhand:RuneShield"));
        var current = Set(0, 10);
        var results = new Dictionary<int, StageResult>
        {
            // The shield alone: a fifth faster; the helmet alone: no faster.
            [0] = Set(1, 10 / 1.2),
            [1] = Set(2, 10),
        };
        var rows = GapRows.Rows(Gear(), swaps, current, results, new Estimate(0.4, 0.35, 0.45, 30, "relative"), 0.01);
        Assert.Equal(2, rows.Count);
        Assert.Equal(Id("RuneShield"), rows[0].Items[0].Id);
        Assert.Equal(0.2, rows[0].Gain.Point, 2);
        Assert.Equal(0.5, rows[0].Share, 2);
        Assert.False(rows[0].OnlyWithSet);
        Assert.True(rows[1].OnlyWithSet);
        Assert.Equal(MarkKind.NotInBank, rows[1].Items[0].Mark!.Kind);
    }
}
