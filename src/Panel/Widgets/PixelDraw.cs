using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// Rectangles and straight lines drawn on whole screen pixels, for the controls CS2 paints itself: edges on pixel
/// edges and thicknesses in whole pixels, never less than one, whatever scale the window is drawn at. They take what
/// the canvas item's own DrawRect and DrawLine take, in the window's units.
/// </summary>
internal static class PixelDraw
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CanvasItem, object> _drawn = [];
    private static readonly object Marker = new();
    private static Vector2 _followed;
    private static CanvasItem? _item;
    private static ulong _frame;
    private static PixelGrid _across;
    private static PixelGrid _down;

    // One item draws many shapes in a frame: its place on screen is read once for them.
    private static void On(CanvasItem item)
    {
        var frame = Godot.Engine.GetProcessFrames();
        if (ReferenceEquals(item, _item) && frame == _frame)
            return;
        var to = item.GetViewport().GetFinalTransform() * item.GetGlobalTransformWithCanvas();
        _across = new PixelGrid(to.Origin.X, to.X.Length());
        _down = new PixelGrid(to.Origin.Y, to.Y.Length());
        _item = item;
        _frame = frame;
        _drawn.TryAdd(item, Marker);
    }

    /// <summary>
    /// Runs once a frame for the window's root: when the scale it is on screen at changed, everything drawn on
    /// whole pixels is drawn again. Each was drawn for the scale it had, and nothing is asked to draw again when
    /// only the scale of what holds it changes.
    /// </summary>
    public static void Follow(Control root)
    {
        var to = root.GetViewport().GetFinalTransform() * root.GetGlobalTransformWithCanvas();
        var scale = new Vector2(to.X.Length(), to.Y.Length());
        if (scale.IsEqualApprox(_followed))
            return;
        _followed = scale;
        PixelBox.Redraw();
        foreach (var (item, _) in _drawn)
        {
            if (GodotObject.IsInstanceValid(item))
                item.QueueRedraw();
        }
    }

    /// <summary>Lets go of the last item drawn on.</summary>
    public static void Clear()
    {
        _item = null;
        _followed = Vector2.Zero;
    }

    /// <summary>
    /// A rectangle: filled, or its outline <paramref name="width"/> units thick about its edges as DrawRect draws it.
    /// </summary>
    public static void PxRect(this CanvasItem item, Rect2 rect, Color colour, bool filled = true, float width = -1)
    {
        On(item);
        if (filled)
        {
            item.DrawRect(Span(rect.Position.X, rect.End.X, rect.Position.Y, rect.End.Y), colour);
            return;
        }
        var units = width > 0 ? width : 1;
        var (x, wide) = (Edge(_across, rect.Position.X, units), Edge(_across, rect.End.X, units));
        var (y, tall) = (Edge(_down, rect.Position.Y, units), Edge(_down, rect.End.Y, units));
        // Four bars that don't overlap, so a see-through colour is as strong at the corners as along the sides.
        item.DrawRect(new Rect2(x.From, y.From, wide.To - x.From, y.To - y.From), colour);
        item.DrawRect(new Rect2(x.From, tall.From, wide.To - x.From, tall.To - tall.From), colour);
        item.DrawRect(new Rect2(x.From, y.To, x.To - x.From, tall.From - y.To), colour);
        item.DrawRect(new Rect2(wide.From, y.To, wide.To - wide.From, tall.From - y.To), colour);
    }

    /// <summary>A line. One that runs straight across or straight down is a bar of whole pixels; any other is drawn as it is.</summary>
    public static void PxLine(this CanvasItem item, Vector2 from, Vector2 to, Color colour, float width = -1)
    {
        On(item);
        var units = width > 0 ? width : 1;
        if (Mathf.IsEqualApprox(from.X, to.X))
        {
            var (left, right) = Edge(_across, from.X, units);
            var top = (float)_down.Snap(Math.Min(from.Y, to.Y));
            item.DrawRect(new Rect2(left, top, right - left, (float)_down.Snap(Math.Max(from.Y, to.Y)) - top), colour);
        }
        else if (Mathf.IsEqualApprox(from.Y, to.Y))
        {
            var (top, bottom) = Edge(_down, from.Y, units);
            var left = (float)_across.Snap(Math.Min(from.X, to.X));
            item.DrawRect(
                new Rect2(left, top, (float)_across.Snap(Math.Max(from.X, to.X)) - left, bottom - top),
                colour
            );
        }
        else
        {
            item.DrawLine(from, to, colour, width);
        }
    }

    // The rectangle between four edges, each on a pixel's edge; one with any size keeps at least a pixel of it.
    private static Rect2 Span(float left, float right, float top, float bottom)
    {
        var x = (float)_across.Snap(left);
        var y = (float)_down.Snap(top);
        var wide = right > left ? Math.Max(_across.Units(1), _across.Snap(right) - x) : 0;
        var tall = bottom > top ? Math.Max(_down.Units(1), _down.Snap(bottom) - y) : 0;
        return new Rect2(x, y, (float)wide, (float)tall);
    }

    // A stroke `units` thick about the position `at`: whole pixels thick, starting on a pixel's edge.
    private static (float From, float To) Edge(PixelGrid grid, float at, float units)
    {
        var from = grid.Snap(at - units / 2);
        return ((float)from, (float)(from + grid.Units(grid.Pixels(units))));
    }
}
