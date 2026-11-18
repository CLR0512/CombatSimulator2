using MelvorMods.CombatSimulator2.Panel.Widgets;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PixelGridTests
{
    // The pixel an edge lands on.
    private static long Pixel(double origin, double scale, double at) => (long)Math.Round(origin + at * scale);

    public static TheoryData<double, double, double> Layouts()
    {
        var data = new TheoryData<double, double, double>();
        foreach (var scale in new[] { 1.0, 0.66667, 1.26796, 1.5, 1.90194 })
        foreach (var origin in new[] { 0.0, 0.37, 411.81 })
        foreach (var slot in new[] { 12.0, 17.3, 24.414635, 27.38, 31.5, 44.0 })
            data.Add(scale, origin, slot);
        return data;
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ABarAndWhatIsDrawnRoundItAreTheSameOnBothSides(double scale, double origin, double slot)
    {
        (long Line, long Outline)? first = null;
        for (var i = 0; i < 40; i++)
        {
            // The chart's positions are single precision; the pixels are counted from the same numbers.
            float x = (float)(56 + i * slot),
                wide = (float)slot;
            long P(double at) => Pixel(origin, scale, at);
            var place = BarSlot.Of(origin, scale, x, wide);
            long slotLeft = P(x),
                slotRight = P(x + wide),
                barLeft = P(place.Left),
                barRight = P(place.Left + place.Width),
                frameLeft = P(place.FrameLeft),
                frameRight = P(place.FrameLeft + place.FrameWidth),
                line = P(place.FrameLeft + place.Line) - frameLeft,
                outline = barLeft - P(place.Left - place.Outline);

            // Every bar of a chart has the same frame and outline, though its slots differ by a pixel.
            first ??= (line, outline);
            Assert.Equal(first, (line, outline));
            Assert.True(barRight > barLeft);
            Assert.Equal(barLeft - slotLeft, slotRight - barRight);
            Assert.Equal(1, frameLeft - slotLeft);
            Assert.Equal(1, slotRight - frameRight);
            Assert.True(line >= 1);
            Assert.True(outline >= 1);
            // The same thickness measured from the other side.
            Assert.Equal(line, frameRight - P(place.FrameLeft + place.FrameWidth - place.Line));
            Assert.Equal(outline, P(place.Left + place.Width + place.Outline) - barRight);
            // With room for a pixel of each, the frame and the outline never meet.
            if (barLeft - slotLeft >= 4)
                Assert.True(barLeft - outline - (frameLeft + line) >= 1);
        }
    }

    [Fact]
    public void NeighbouringSlotsShareTheEdgeBetweenThem()
    {
        var grid = new PixelGrid(0.4, 1.26796);
        const double Slot = 27.38;
        for (var i = 0; i < 40; i++)
            Assert.Equal(grid.Snap(56 + (i + 1) * Slot), grid.Snap(56 + i * Slot + Slot), 9);
    }

    [Theory]
    [InlineData(1.0, 2, 2)]
    [InlineData(1.26796, 2, 3)]
    [InlineData(0.66667, 2, 1)]
    [InlineData(0.2, 1, 1)]
    public void ALengthIsWholePixelsAndNeverNone(double scale, double units, long pixels) =>
        Assert.Equal(pixels, new PixelGrid(0, scale).Pixels(units));

    [Fact]
    public void AGridWithoutAScaleIsOneToOne()
    {
        var grid = new PixelGrid(0, 0);
        Assert.Equal(3, grid.Snap(3.2));
        Assert.Equal(4, grid.Between(0, 4));
        Assert.Equal(2, grid.Units(2));
    }
}
