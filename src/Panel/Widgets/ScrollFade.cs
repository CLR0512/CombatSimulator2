using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A scrolling area that fades out at an end while there is more past it: the hint that it scrolls. Gone once scrolled
/// to that end.
/// </summary>
internal static class ScrollFade
{
    private const float Height = 30;

    // A band across the holder from y0 to y1, its colour going from the first to the second.
    private static void Band(Control holder, float y0, float y1, Color from, Color to)
    {
        if (y1 <= y0)
            return;
        var width = holder.Size.X;
        holder.DrawPolygon(
            [new Vector2(0, y0), new Vector2(width, y0), new Vector2(width, y1), new Vector2(0, y1)],
            [from, from, to, to]
        );
    }

    /// <summary>
    /// <paramref name="scroll"/> in a container of its own whose ends fade the list itself out while there is more
    /// past them, so it blends into whatever is behind it (a dark list panel shows a black fade as a band); add that
    /// instead.
    /// </summary>
    public static MarginContainer Wrap(ScrollContainer scroll)
    {
        var holder = new MarginContainer
        {
            SizeFlagsHorizontal = scroll.SizeFlagsHorizontal,
            SizeFlagsVertical = scroll.SizeFlagsVertical,
        };
        holder.AddChild(scroll);
        var bar = scroll.GetVScrollBar();
        // The holder's drawing is the list's mask. Masked only while there is more above or below, as a mask draws the
        // list through a buffer of its own.
        void Follow()
        {
            var more = bar.Value > 1 || bar.Value + bar.Page < bar.MaxValue - 1;
            var mode = more ? CanvasItem.ClipChildrenMode.Only : CanvasItem.ClipChildrenMode.Disabled;
            if (holder.ClipChildren != mode)
                holder.ClipChildren = mode;
            holder.QueueRedraw();
        }
        holder.Draw += () =>
        {
            if (holder.ClipChildren == CanvasItem.ClipChildrenMode.Disabled)
                return;
            var size = holder.Size;
            // Clear at an end with more past it, opaque between: the list fades out at the top once scrolled down,
            // and at the foot while more is below.
            var above = bar.Value > 1 ? Math.Min(Height, size.Y / 2) : 0;
            var below = bar.Value + bar.Page < bar.MaxValue - 1 ? Math.Min(Height, size.Y / 2) : 0;
            var solid = Colors.White;
            var clear = Colors.White with { A = 0 };
            Band(holder, 0, above, clear, solid);
            holder.DrawRect(new Rect2(0, above, size.X, size.Y - above - below), solid);
            Band(holder, size.Y - below, size.Y, solid, clear);
        };
        bar.ValueChanged += _ => Follow();
        bar.Changed += Follow;
        holder.Resized += Follow;
        return holder;
    }

    /// <summary>
    /// As <see cref="Wrap"/>, for a chart that scrolls sideways: its left and right edges fade while there is more past
    /// them, over <paramref name="edge"/> (a chart's column, so one cut short fades as a whole rather than showing a
    /// sliver). Its scrollbar and the strip <paramref name="solidBelow"/> tall over it (a legend) stay whole.
    /// </summary>
    public static MarginContainer Sideways(ScrollContainer scroll, float solidBelow = 0, float edge = Height)
    {
        var holder = new MarginContainer
        {
            SizeFlagsHorizontal = scroll.SizeFlagsHorizontal,
            SizeFlagsVertical = scroll.SizeFlagsVertical,
        };
        holder.AddChild(scroll);
        // No box of its own: the mask would fade it with the bars, a lighter band at each edge.
        PixelBox.Panel(scroll, new StyleBoxEmpty());
        var bar = scroll.GetHScrollBar();
        void Follow()
        {
            var more = bar.Value > 1 || bar.Value + bar.Page < bar.MaxValue - 1;
            var mode = more ? CanvasItem.ClipChildrenMode.Only : CanvasItem.ClipChildrenMode.Disabled;
            if (holder.ClipChildren != mode)
                holder.ClipChildren = mode;
            holder.QueueRedraw();
        }
        holder.Draw += () =>
        {
            if (holder.ClipChildren == CanvasItem.ClipChildrenMode.Disabled)
                return;
            var size = holder.Size;
            // The bar as drawn: its control can be taller than its box, over the chart's last row.
            var foot = Math.Clamp(size.Y - solidBelow - (bar.Visible ? bar.GetCombinedMinimumSize().Y : 0), 0, size.Y);
            var left = bar.Value > 1 ? Math.Min(edge, size.X / 2) : 0;
            var right = bar.Value + bar.Page < bar.MaxValue - 1 ? Math.Min(edge, size.X / 2) : 0;
            var solid = Colors.White;
            var clear = Colors.White with { A = 0 };
            Across(holder, 0, left, foot, clear, solid);
            holder.DrawRect(new Rect2(left, 0, size.X - left - right, foot), solid);
            Across(holder, size.X - right, size.X, foot, solid, clear);
            holder.DrawRect(new Rect2(0, foot, size.X, size.Y - foot), solid);
        };
        bar.ValueChanged += _ => Follow();
        bar.Changed += Follow;
        holder.Resized += Follow;
        return holder;
    }

    // A band down the holder from x0 to x1 and from its top to y, its colour going from the first to the second.
    private static void Across(Control holder, float x0, float x1, float y, Color from, Color to)
    {
        if (x1 <= x0 || y <= 0)
            return;
        holder.DrawPolygon(
            [new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, y), new Vector2(x0, y)],
            [from, to, to, from]
        );
    }
}
