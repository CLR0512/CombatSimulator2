namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// The Scale setting's steps, and the cap a room on screen puts on them: the window is never drawn so large that the
/// smallest supported room no longer fits in it.
/// </summary>
public static class ScaleSteps
{
    public const double Default = 1.00;
    public const double Smallest = 0.70;
    public const double Largest = 1.50;

    // Written out rather than added up, so a saved 1.1 reads back as exactly the step 1.1.
    private static readonly double[] _steps =
    [
        0.70,
        0.75,
        0.80,
        0.85,
        0.90,
        0.95,
        1.00,
        1.05,
        1.10,
        1.15,
        1.20,
        1.25,
        1.30,
        1.35,
        1.40,
        1.45,
        1.50,
    ];

    public static IReadOnlyList<double> Steps => _steps;

    /// <summary>
    /// The smallest room, in design units at 100%, that every view fits without scrolling (title bar, tabs and the
    /// view): the room the game's own smallest layout (1600 × 900, or 1080p at a UI scale of 1.2) leaves beside its
    /// header, sidebar and footer. The cap keeps at least this much room, and each view is built to fit it.
    /// </summary>
    public static (double Width, double Height) SmallestRoom { get; } = (1536, 780);

    /// <summary>The step nearest <paramref name="value"/>; a value that isn't a number is the default.</summary>
    public static double Snap(double value)
    {
        if (!double.IsFinite(value))
            return Default;
        var nearest = _steps[0];
        foreach (var step in _steps)
        {
            if (Math.Abs(step - value) < Math.Abs(nearest - value))
                nearest = step;
        }
        return nearest;
    }

    /// <summary>
    /// The largest step at which a room of <paramref name="roomWidth"/> × <paramref name="roomHeight"/> on screen,
    /// drawn at <paramref name="baseScale"/> times the step, still holds the smallest room on both axes. The smallest
    /// step when none does: the views scroll then.
    /// </summary>
    public static double Cap(
        double roomWidth,
        double roomHeight,
        double baseScale,
        double smallestWidth,
        double smallestHeight
    )
    {
        // Rounding in the room's units must not lose a step that fits exactly.
        const double Tolerance = 1e-9;
        for (var i = _steps.Length - 1; i > 0; i--)
        {
            var drawn = baseScale * _steps[i];
            if (
                roomWidth / drawn >= smallestWidth * (1 - Tolerance)
                && roomHeight / drawn >= smallestHeight * (1 - Tolerance)
            )
                return _steps[i];
        }
        return Smallest;
    }

    // How near a whole number of pixels a unit must come to be drawn at exactly that.
    private const double CrispWithin = 0.03;

    /// <summary>
    /// The scale to draw <paramref name="scale"/> at on a screen where one unit at 100% takes
    /// <paramref name="stretch"/> pixels. A scale that brings a unit within a few percent of one pixel, or of two, is
    /// drawn at exactly that: every line and gap is then whole pixels, which a hair's difference in size is worth.
    /// </summary>
    public static double Crisp(double scale, double stretch)
    {
        if (!(stretch > 0) || !double.IsFinite(stretch))
            return scale;
        foreach (var whole in (double[])[1, 2])
        {
            if (Math.Abs(scale * stretch - whole) <= whole * CrispWithin)
                return whole / stretch;
        }
        return scale;
    }

    /// <summary>The scale drawn: the saved one, or the cap when the room allows less.</summary>
    public static double Drawn(double saved, double cap) => Math.Min(saved, cap);

    /// <summary>The step below <paramref name="current"/>, never under the smallest.</summary>
    public static double Down(double current)
    {
        var index = Array.IndexOf(_steps, Snap(current));
        return _steps[Math.Max(0, index - 1)];
    }

    /// <summary>The step above <paramref name="current"/>, never past <paramref name="cap"/>.</summary>
    public static double Up(double current, double cap)
    {
        var index = Array.IndexOf(_steps, Snap(current));
        return Math.Min(_steps[Math.Min(_steps.Length - 1, index + 1)], Snap(cap));
    }
}
