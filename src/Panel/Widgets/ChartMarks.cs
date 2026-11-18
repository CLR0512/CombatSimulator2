using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>What the chart draws besides bars: the stub of a target where every fight died, and the legend.</summary>
internal static class ChartMarks
{
    private const float StubHeight = 10;

    /// <summary>
    /// A short stub at the base of a bar's slot with the "!" mark over it, in place of a bar: the target's value was
    /// earned while dying, so its height would say the wrong thing.
    /// </summary>
    public static void Stub(CanvasItem canvas, float left, float width, float baseY, Color colour)
    {
        canvas.PxRect(new Rect2(left, baseY - StubHeight, width, StubHeight), colour);
        if (width < 10)
            return;
        var side = Math.Min(width, 16);
        var box = new Rect2(left + (width - side) / 2, baseY - StubHeight - side - 3, side, side);
        canvas.PxRect(box, Look.DeadlyFill);
        canvas.PxRect(box, colour, false, 1);
        Bang(canvas, box, colour.Lerp(Colors.White, 0.75f));
    }

    // The "!" drawn as a stroke and a dot, centred in the box both ways: as text it sits where the font's baseline
    // falls, which is low in a box this small.
    private static void Bang(CanvasItem canvas, Rect2 box, Color colour)
    {
        var stroke = Mathf.Max(1.5f, Mathf.Round(box.Size.X / 8));
        var height = box.Size.Y * 0.6f;
        var dot = stroke;
        var gap = stroke;
        var x = box.GetCenter().X - stroke / 2;
        var top = box.GetCenter().Y - height / 2;
        canvas.PxRect(new Rect2(x, top, stroke, height - dot - gap), colour);
        canvas.PxRect(new Rect2(x, top + height - dot, stroke, dot), colour);
    }

    /// <summary>
    /// The legend, left to right from <paramref name="at"/>: the deaths colours, the died stub, the ran-over marks,
    /// best, the whisker.
    /// </summary>
    public static void Legend(CanvasItem canvas, Font font, Vector2 at, Color text)
    {
        var x = at.X;
        foreach (
            var (colour, label, mark) in new[]
            {
                (Look.Safe, Tr.Get("no deaths"), Mark.Swatch),
                (Look.Risky, Tr.Get("some deaths"), Mark.Swatch),
                (Look.Deadly, Tr.Get("5%+ deaths"), Mark.Swatch),
                (Look.Deadly, Tr.Get("died every fight"), Mark.Died),
                // Translators: Chart legend: the mark of a target where some trials ran over the time limit (under 5%).
                (Look.RanOver, Tr.Get("some ran over"), Mark.Swatch),
                // Translators: Chart legend: the mark of a target where 5% or more of the trials ran over the time limit.
                (Look.ManyRanOver, Tr.Get("5%+ ran over"), Mark.Swatch),
                (Look.Best, Tr.Get("best"), Mark.Swatch),
                (Colors.White, Tr.Get("95% range"), Mark.Whisker),
            }
        )
        {
            var swatch = new Rect2(x, at.Y - 9, 10, 10);
            if (mark == Mark.Whisker)
                Whisker(canvas, swatch.Grow(1), colour, 1.5f);
            else if (mark == Mark.Died)
            {
                canvas.PxRect(swatch, Look.DeadlyFill);
                canvas.PxRect(swatch, colour, false, 1);
                Bang(canvas, swatch, Colors.White);
            }
            else
                canvas.PxRect(swatch, colour);
            canvas.DrawString(font, new Vector2(x + 14, at.Y), label, HorizontalAlignment.Left, -1, 12, text);
            x += 22 + font.GetStringSize(label, HorizontalAlignment.Left, -1, 12).X + 12;
        }
    }

    private enum Mark
    {
        Swatch,
        Died,
        Whisker,
    }

    /// <summary>
    /// The legend's whisker: a line down the middle of <paramref name="box"/> with a cap at each end, as the bars draw
    /// their 95% range.
    /// </summary>
    public static void Whisker(CanvasItem canvas, Rect2 box, Color colour, float width)
    {
        var x = Mathf.Round(box.GetCenter().X);
        var cap = Mathf.Min(4, box.Size.X / 2);
        var top = box.Position.Y + width / 2;
        var bottom = box.End.Y - width / 2;
        canvas.PxLine(new Vector2(x, top), new Vector2(x, bottom), colour, width);
        canvas.PxLine(new Vector2(x - cap, top), new Vector2(x + cap, top), colour, width);
        canvas.PxLine(new Vector2(x - cap, bottom), new Vector2(x + cap, bottom), colour, width);
    }

    /// <summary>
    /// Some trials ran over: a strip in the chip's colour along the inside of the bar's right edge, drawn after the
    /// selection so its outline never hides it. Nothing without a bar or a share.
    /// </summary>
    public static void RanOver(CanvasItem canvas, Rect2? bar, double? share)
    {
        if (bar is { } r && share is { } s)
        {
            // On whole screen pixels, where the bar's own edge lands: the panel is drawn scaled, so a whole pixel of the
            // chart's can end short of the bar's edge. Every bar's strip is then the same width wherever its edge falls.
            // The viewport's own stretch included: the game draws at a base size and scales that to the window.
            var toScreen = canvas.GetViewport().GetFinalTransform() * canvas.GetGlobalTransformWithCanvas();
            var fromScreen = toScreen.AffineInverse();
            var edge = toScreen * r.End;
            var right = Mathf.Round(edge.X);
            var width = Mathf.Max(1, Mathf.Round(3 * toScreen.Scale.X));
            var x0 = (fromScreen * new Vector2(right - width, edge.Y)).X;
            var x1 = (fromScreen * new Vector2(right, edge.Y)).X;
            canvas.PxRect(new Rect2(x0, r.Position.Y, x1 - x0, r.Size.Y), Look.RanOverColour(s));
        }
    }

    /// <summary>The gold diamond over a pinned target's bar.</summary>
    public static void Pip(CanvasItem canvas, Vector2 centre)
    {
        // On a screen pixel's centre, its points a whole number of pixels out, so its edges step one pixel per row
        // and look the same over every bar; a small circle is either jagged or blurred at the panel's scale.
        var toScreen = canvas.GetViewport().GetFinalTransform() * canvas.GetGlobalTransformWithCanvas();
        var fromScreen = toScreen.AffineInverse();
        var reach = Mathf.Max(3, Mathf.Round(PipReach * toScreen.Scale.X));
        var at = toScreen * centre;
        var middle = new Vector2(Mathf.Floor(at.X) + 0.5f, Mathf.Floor(at.Y) + 0.5f);
        canvas.DrawColoredPolygon(
            [
                fromScreen * (middle + new Vector2(0, -reach)),
                fromScreen * (middle + new Vector2(reach, 0)),
                fromScreen * (middle + new Vector2(0, reach)),
                fromScreen * (middle + new Vector2(-reach, 0)),
            ],
            Look.Best
        );
    }

    private const float PipReach = 4;

    /// <summary>The bars that died, stalled or ran over, for <c>cs2 ui</c>'s chart line.</summary>
    public static string Counts(Chart chart) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $" died={chart.Died} stalled={chart.Stalls.Stalled} ran-over={chart.Stalls.RanOver}"
        );
}
