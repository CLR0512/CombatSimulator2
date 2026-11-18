namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// Where a chart bar goes across its slot, with what may be drawn round it, all on whole screen pixels and the same
/// on both sides. From the slot's edge inwards: one pixel free, the selection's frame, at least one pixel free, the
/// outline of a bar that stands for a collapsed area, then the bar. The frame and the outline are each drawn
/// <paramref name="Line"/> and <paramref name="Outline"/> thick inside those places; they thin down to a pixel
/// before a narrow slot makes them meet.
/// </summary>
internal readonly record struct BarSlot(
    float Left,
    float Width,
    float FrameLeft,
    float FrameWidth,
    float Line,
    float Outline
)
{
    private const double BarShare = 0.72;
    private const double NarrowestBar = 4;
    private const double FrameLine = 2;
    private const double OutlineLine = 1.5;
    private const long FrameInset = 1;
    private const long Gap = 1;

    // The bar's pixels in a slot of `room` pixels: its share, with the same number of pixels free on each side.
    private static long Bar(PixelGrid pixels, long room)
    {
        room = Math.Max(1, room);
        var bar = Math.Clamp((long)Math.Round(room * BarShare), Math.Min(room, pixels.Pixels(NarrowestBar)), room);
        // An odd number of free pixels can't be shared equally: the bar gives one up, or takes one when it has none
        // to give.
        if ((room - bar) % 2 != 0)
            bar += bar > 1 ? -1 : 1;
        return bar;
    }

    /// <summary>
    /// The places for the slot from <paramref name="x"/>, <paramref name="slot"/> wide, on a canvas whose zero is at
    /// <paramref name="origin"/> on screen and whose units are <paramref name="scale"/> pixels.
    /// </summary>
    public static BarSlot Of(double origin, double scale, float x, float slot)
    {
        var pixels = new PixelGrid(origin, scale);
        var from = pixels.Snap(x);
        var room = Math.Max(1, pixels.Between(x, x + slot));
        var bar = Bar(pixels, room);
        var side = (room - bar) / 2;
        // Slots of one width come out a pixel apart on screen, as their edges round. The thicknesses are chosen for
        // the tighter of the two, so every bar of the chart has the same frame and outline.
        var least = (long)Math.Floor(slot * scale);
        var tightest = Math.Min((least - Bar(pixels, least)) / 2, (least + 1 - Bar(pixels, least + 1)) / 2);
        var line = pixels.Pixels(FrameLine);
        var outline = pixels.Pixels(OutlineLine);
        while (FrameInset + line + Gap + outline > tightest && (line > 1 || outline > 1))
        {
            if (outline > 1)
                outline--;
            else
                line--;
        }
        return new(
            (float)(from + pixels.Units(side)),
            (float)pixels.Units(bar),
            (float)(from + pixels.Units(FrameInset)),
            (float)pixels.Units(Math.Max(1, room - 2 * FrameInset)),
            (float)pixels.Units(line),
            (float)pixels.Units(outline)
        );
    }
}
