using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>A horizontal bar on the game's progress-bar track: a fill fraction and an optional median tick.</summary>
internal static class Bar
{
    private const string FractionMeta = "cs2_fraction";
    private const string ShownMeta = "cs2_shown";
    private const string ColourMeta = "cs2_colour";
    private const string TickMeta = "cs2_tick";
    private const string RunningMeta = "cs2_running";

    public static Control Make(string name = "", float height = 8)
    {
        var bar = new Control
        {
            CustomMinimumSize = new Vector2(40, height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (name.Length > 0)
            bar.Name = name;
        bar.Draw += () => Paint(bar);
        bar.ThemeChanged += bar.QueueRedraw;
        return bar;
    }

    /// <summary>
    /// Sets the fill (0…1), its colour and the median tick (null for none). The fill eases to its new length;
    /// <paramref name="running"/> draws a sheen moving back and forth along it instead, for a target still being simulated.
    /// </summary>
    public static void Set(Control bar, double fraction, Color colour, double? tick = null, bool running = false)
    {
        var target = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        var shown = bar.HasMeta(ShownMeta) ? bar.GetMeta(ShownMeta).AsDouble() : 0;
        bar.SetMeta(FractionMeta, target);
        bar.SetMeta(ColourMeta, colour);
        bar.SetMeta(TickMeta, tick is { } t && double.IsFinite(t) ? Math.Clamp(t, 0, 1) : -1.0);
        bar.SetMeta(RunningMeta, running);
        Motion.Ease(
            bar,
            shown,
            target,
            MotionKind.Fill,
            v =>
            {
                bar.SetMeta(ShownMeta, v);
                bar.QueueRedraw();
            }
        );
    }

    private static void PaintMedian(Control bar, float x, float height) => MedianMark.Paint(bar, x, 0, height);

    public static double Fraction(Control bar) => bar.HasMeta(FractionMeta) ? bar.GetMeta(FractionMeta).AsDouble() : 0;

    private static void Paint(Control bar)
    {
        var size = bar.Size;
        var track = new Rect2(Vector2.Zero, size);
        if (Look.Track(bar) is StyleBoxTexture or null)
            bar.PxRect(track, Colors.Black with { A = 0.55f });
        else
            Look.Track(bar).Draw(bar.GetCanvasItem(), track);
        var fraction = (float)(bar.HasMeta(ShownMeta) ? bar.GetMeta(ShownMeta).AsDouble() : Fraction(bar));
        var colour = bar.HasMeta(ColourMeta) ? bar.GetMeta(ColourMeta).AsColor() : Look.BarFill;
        if (fraction > 0)
            bar.PxRect(new Rect2(0, 0, Math.Max(2, size.X * fraction), size.Y), colour);
        if (
            bar.HasMeta(RunningMeta)
            && bar.GetMeta(RunningMeta).AsBool()
            && bar.IsVisibleInTree()
            && Motion.Phase(MotionKind.Sheen) is { } phase
        )
        {
            // A soft band gliding from end to end and back, slowing at each end; redrawn each frame while running.
            // One gradient at its exact place, not snapped to pixels: strips snapped one by one shimmer as they go.
            var band = Math.Min(size.X, Math.Max(24, size.X * 0.18f));
            var there = 0.5f - 0.5f * MathF.Cos(phase * MathF.Tau);
            var x = there * (size.X - band);
            var clear = Colors.White with { A = 0 };
            var lit = Colors.White with { A = 0.3f };
            float left = x,
                middle = x + band / 2,
                right = x + band;
            bar.DrawPolygon(
                [new(left, 0), new(middle, 0), new(middle, size.Y), new(left, size.Y)],
                [clear, lit, lit, clear]
            );
            bar.DrawPolygon(
                [new(middle, 0), new(right, 0), new(right, size.Y), new(middle, size.Y)],
                [lit, clear, clear, lit]
            );
            bar.QueueRedraw();
        }
        var tick = bar.HasMeta(TickMeta) ? (float)bar.GetMeta(TickMeta).AsDouble() : -1;
        if (tick >= 0)
            PaintMedian(bar, Math.Clamp(size.X * tick, 2, size.X - 2), size.Y);
    }
}

/// <summary>The median marker shared by bars: an outlined gold tick, taller than the bar, under a triangle.</summary>
internal static class MedianMark
{
    public static void Paint(CanvasItem item, float x, float top, float bottom)
    {
        var outline = Colors.Black with { A = 0.7f };
        item.PxLine(new Vector2(x, top - 1), new Vector2(x, bottom + 1), outline, 3.5f);
        item.PxLine(new Vector2(x, top - 1), new Vector2(x, bottom + 1), Look.Best, 1.5f);
        item.DrawColoredPolygon([new(x - 3.5f, top - 6), new(x + 3.5f, top - 6), new(x, top - 1)], outline);
        item.DrawColoredPolygon([new(x - 2.5f, top - 5.5f), new(x + 2.5f, top - 5.5f), new(x, top - 1.5f)], Look.Best);
    }
}
