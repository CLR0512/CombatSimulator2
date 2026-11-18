namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>A rectangle in the window's units, top-left origin, without Godot so placement can be tested.</summary>
internal readonly record struct TourRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public TourRect Grow(float by) => new(X - by, Y - by, Width + 2 * by, Height + 2 * by);
}

/// <summary>Where a tour popover goes relative to the control it explains.</summary>
internal enum TourSide
{
    Right,
    Below,
    Left,
    Above,

    /// <summary>No side had room: the popover covers the far end of the control.</summary>
    Over,
}

/// <summary>
/// Places a tour popover beside its highlighted control: the first side with room, in the order right, below, left,
/// above (or <c>first</c>, then that order), a gap away from the ring and lined up with its top (beside) or left edge (above or below), then kept inside
/// the window. When no side has room it goes over the control's far end rather than out of the window.
/// </summary>
internal static class TourPlacement
{
    public static (float X, float Y, TourSide Side) Place(
        TourRect window,
        TourRect ring,
        float width,
        float height,
        TourSide first = TourSide.Right,
        float gap = 16,
        float margin = 8
    )
    {
        var inner = new TourRect(
            window.X + margin,
            window.Y + margin,
            window.Width - 2 * margin,
            window.Height - 2 * margin
        );
        foreach (var side in (TourSide[])[first, TourSide.Right, TourSide.Below, TourSide.Left, TourSide.Above])
        {
            var fits = side switch
            {
                TourSide.Right => ring.Right + gap + width <= inner.Right,
                TourSide.Below => ring.Bottom + gap + height <= inner.Bottom,
                TourSide.Left => ring.X - gap - width >= inner.X,
                TourSide.Above => ring.Y - gap - height >= inner.Y,
                _ => false,
            };
            if (!fits)
                continue;
            return side switch
            {
                TourSide.Right => Fit(inner, ring.Right + gap, ring.Y, width, height, side),
                TourSide.Below => Fit(inner, ring.X, ring.Bottom + gap, width, height, side),
                TourSide.Left => Fit(inner, ring.X - gap - width, ring.Y, width, height, side),
                _ => Fit(inner, ring.X, ring.Y - gap - height, width, height, side),
            };
        }
        return Fit(inner, ring.Right - width, ring.Bottom - height, width, height, TourSide.Over);
    }

    // Moved, not resized, to stay inside; a popover larger than the room sits at its top-left.
    private static (float, float, TourSide) Fit(
        TourRect inner,
        float x,
        float y,
        float width,
        float height,
        TourSide side
    ) =>
        (
            Math.Max(inner.X, Math.Min(x, inner.Right - width)),
            Math.Max(inner.Y, Math.Min(y, inner.Bottom - height)),
            side
        );
}
