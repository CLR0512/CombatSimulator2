using MelvorMods.CombatSimulator2.Panel.Widgets;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class TourPlacementTests
{
    private static readonly TourRect Window = new(0, 0, 1600, 900);

    [Fact]
    public void BesideOnTheRightLinedUpWithTheTop()
    {
        var (x, y, side) = TourPlacement.Place(Window, new TourRect(0, 120, 312, 780), 340, 205);
        Assert.Equal(TourSide.Right, side);
        Assert.Equal(328, x);
        Assert.Equal(120, y);
    }

    [Fact]
    public void BelowWhenTheRightHasNoRoom()
    {
        var (x, y, side) = TourPlacement.Place(Window, new TourRect(1340, 60, 250, 54), 340, 205);
        Assert.Equal(TourSide.Below, side);
        Assert.Equal(130, y);
        Assert.Equal(1600 - 8 - 340, x); // kept inside on the right
    }

    [Fact]
    public void LeftWhenRightAndBelowHaveNoRoom()
    {
        var (x, y, side) = TourPlacement.Place(Window, new TourRect(1300, 100, 290, 790), 340, 205);
        Assert.Equal(TourSide.Left, side);
        Assert.Equal(1300 - 16 - 340, x);
        Assert.Equal(100, y);
    }

    [Fact]
    public void AboveWhenOnlyAboveHasRoom()
    {
        var (_, y, side) = TourPlacement.Place(Window, new TourRect(10, 600, 1580, 290), 340, 205);
        Assert.Equal(TourSide.Above, side);
        Assert.Equal(600 - 16 - 205, y);
    }

    [Fact]
    public void OverTheFarEndWhenNoSideHasRoomAndStillInside()
    {
        var (x, y, side) = TourPlacement.Place(Window, new TourRect(0, 0, 1600, 900), 340, 205);
        Assert.Equal(TourSide.Over, side);
        Assert.Equal(1600 - 8 - 340, x);
        Assert.Equal(900 - 8 - 205, y);
    }

    [Theory]
    [InlineData(-50, -50, 100, 100)]
    [InlineData(1500, 850, 200, 200)]
    [InlineData(700, 400, 100, 40)]
    public void AlwaysInsideTheWindowMargin(float rx, float ry, float rw, float rh)
    {
        var (x, y, _) = TourPlacement.Place(Window, new TourRect(rx, ry, rw, rh), 340, 205);
        Assert.InRange(x, 8, 1600 - 8 - 340);
        Assert.InRange(y, 8, 900 - 8 - 205);
    }
}
