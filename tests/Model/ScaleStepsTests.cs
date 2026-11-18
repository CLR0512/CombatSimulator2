using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ScaleStepsTests
{
    // A smallest room and a base for the cap cases, apart from the shipped ones so the cases keep their meaning.
    private const double SmallestWidth = 1000;
    private const double SmallestHeight = 600;
    private const double Base = 1.25;

    [Fact]
    public void TheStepsRunFrom70To150InFives()
    {
        Assert.Equal(17, ScaleSteps.Steps.Count);
        Assert.Equal(0.70, ScaleSteps.Steps[0]);
        Assert.Equal(1.50, ScaleSteps.Steps[^1]);
        Assert.Contains(1.10, ScaleSteps.Steps);
        Assert.Equal(1.00, ScaleSteps.Default);
    }

    [Theory]
    [InlineData(0.1, 0.70)]
    [InlineData(0.69, 0.70)]
    [InlineData(0.70, 0.70)]
    [InlineData(1.02, 1.00)]
    [InlineData(1.04, 1.05)]
    [InlineData(1.1, 1.10)]
    [InlineData(1.51, 1.50)]
    [InlineData(9, 1.50)]
    public void SnapGoesToTheNearestStepWithinTheEnds(double value, double step) =>
        Assert.Equal(step, ScaleSteps.Snap(value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SnapTakesTheDefaultForAValueThatIsNotANumber(double value) =>
        Assert.Equal(ScaleSteps.Default, ScaleSteps.Snap(value));

    [Fact]
    public void ARoomThatHoldsTheSmallestAt150PercentIsCappedAt150() =>
        Assert.Equal(1.50, Cap(SmallestWidth * Base * 1.5, SmallestHeight * Base * 1.5));

    [Fact]
    public void ARoomThatHoldsTheSmallestAt115PercentIsCappedThere()
    {
        Assert.Equal(1.15, Cap(SmallestWidth * Base * 1.15, SmallestHeight * Base * 2));
        Assert.Equal(1.15, Cap(SmallestWidth * Base * 2, SmallestHeight * Base * 1.17));
    }

    [Fact]
    public void ARoomThatHoldsNothingIsCappedAtTheSmallestStep() => Assert.Equal(0.70, Cap(200, 100));

    [Fact]
    public void TheCapNeverFallsAsTheRoomGrows()
    {
        var last = 0.0;
        for (var width = 500.0; width <= 3000; width += 7)
        {
            var cap = Cap(width, width * 0.6);
            Assert.True(cap >= last, $"cap {cap} at width {width} is below {last}");
            last = cap;
        }
        Assert.Equal(1.50, last);
    }

    [Theory]
    [InlineData(1.20, 1.50, 1.20)]
    [InlineData(1.50, 1.20, 1.20)]
    [InlineData(0.70, 0.70, 0.70)]
    public void TheDrawnScaleIsTheSavedOneUpToTheCap(double saved, double cap, double drawn) =>
        Assert.Equal(drawn, ScaleSteps.Drawn(saved, cap));

    [Fact]
    public void UpStepsOnceAndStopsAtTheCap()
    {
        Assert.Equal(1.05, ScaleSteps.Up(1.00, 1.50));
        Assert.Equal(1.20, ScaleSteps.Up(1.20, 1.20));
        Assert.Equal(1.50, ScaleSteps.Up(1.50, 1.50));
    }

    [Fact]
    public void DownStepsOnceAndStopsAt70Percent()
    {
        Assert.Equal(0.95, ScaleSteps.Down(1.00));
        Assert.Equal(0.70, ScaleSteps.Down(0.75));
        Assert.Equal(0.70, ScaleSteps.Down(0.70));
    }

    [Theory]
    // 80% on a 1440p screen is a hair over a pixel a unit: drawn at exactly one.
    [InlineData(0.80, 1.26796, 1 / 1.26796)]
    [InlineData(1.00, 1.0, 1.0)]
    [InlineData(1.00, 0.98, 1 / 0.98)]
    [InlineData(1.55, 1.26796, 2 / 1.26796)]
    // Further off, the scale is the one chosen.
    [InlineData(0.75, 1.26796, 0.75)]
    [InlineData(1.00, 1.26796, 1.00)]
    [InlineData(0.70, 1.0, 0.70)]
    [InlineData(1.50, 1.0, 1.50)]
    public void AScaleNearAWholePixelPerUnitIsDrawnAtIt(double scale, double stretch, double drawn) =>
        Assert.Equal(drawn, ScaleSteps.Crisp(scale, stretch), 9);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void WithoutAStretchTheScaleIsTheOneChosen(double stretch) =>
        Assert.Equal(0.8, ScaleSteps.Crisp(0.8, stretch));

    private static double Cap(double width, double height) =>
        ScaleSteps.Cap(width, height, Base, SmallestWidth, SmallestHeight);
}
