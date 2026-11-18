namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// The screen's pixels along one axis, as a canvas sees them: where the canvas's zero lands on screen and how many
/// pixels one of its units takes. A canvas draws in units that rarely fall on whole pixels (the game's UI scale and
/// CS2's own both stretch it), and an edge drawn between two pixels is rounded to one of them on its own, so gaps
/// that are equal in units come out unequal on screen. What is laid out through here is counted in whole pixels.
/// </summary>
internal readonly record struct PixelGrid(double Origin, double Scale)
{
    private double PerUnit => Scale > 0 && double.IsFinite(Scale) ? Scale : 1;

    /// <summary>The position nearest <paramref name="at"/> that is on a pixel's edge.</summary>
    public double Snap(double at) => (Math.Round(Origin + at * PerUnit) - Origin) / PerUnit;

    /// <summary>The whole pixels between the pixel edges nearest <paramref name="start"/> and <paramref name="end"/>.</summary>
    public long Between(double start, double end) =>
        (long)Math.Round(Origin + end * PerUnit) - (long)Math.Round(Origin + start * PerUnit);

    /// <summary>A length of <paramref name="units"/> in whole pixels, at least one.</summary>
    public long Pixels(double units) => Math.Max(1, (long)Math.Round(units * PerUnit));

    /// <summary>The length of <paramref name="pixels"/> pixels in the canvas's units.</summary>
    public double Units(long pixels) => pixels / PerUnit;
}
