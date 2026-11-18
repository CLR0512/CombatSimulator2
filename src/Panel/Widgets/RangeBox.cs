using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// One target against the run's others, from the worst value (left) to the best (right): whiskers over the whole
/// range, a box over the middle half of the targets with their median, and a diamond for this one.
/// </summary>
internal static class RangeBox
{
    public const float Height = 14;
    private const float Pad = 6; // half the diamond, so it fits at either end
    private const string SpotMeta = "cs2_box_spot";
    private const string BoxMeta = "cs2_box_quartiles";

    // Where each named box was last drawn. The details rebuild their rows as a run's targets finish, so a new box
    // starts from where the old one was and slides on rather than jumping.
    private static readonly Dictionary<string, (float Spot, Vector3 Box)> _last = new(StringComparer.Ordinal);

    public static Control Make(string name = "")
    {
        var box = new Control
        {
            CustomMinimumSize = new Vector2(40, Height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (name.Length > 0)
            box.Name = name;
        box.Draw += () => Paint(box);
        return box;
    }

    /// <summary>
    /// Places this target and the run's quartiles, 0…1. A named box eases from where the last box of its name was.
    /// </summary>
    public static void Set(Control box, double spot, (double Low, double Median, double High) quartiles)
    {
        static float Clamp(double v) => (float)Math.Clamp(double.IsFinite(v) ? v : 0, 0, 1);
        var to = (
            Spot: Clamp(spot),
            Box: new Vector3(Clamp(quartiles.Low), Clamp(quartiles.Median), Clamp(quartiles.High))
        );
        var name = box.Name.ToString();
        if (name.Length == 0 || !_last.TryGetValue(name, out var from))
            from = to;
        if (name.Length > 0)
            _last[name] = to;
        void Show(double t)
        {
            var at = (float)t;
            box.SetMeta(SpotMeta, Mathf.Lerp(from.Spot, to.Spot, at));
            box.SetMeta(BoxMeta, from.Box.Lerp(to.Box, at));
            box.QueueRedraw();
        }
        Show(0);
        // A new box isn't in the tree yet, and easing skips what isn't showing: it starts once the box is in.
        if (box.IsInsideTree())
            Motion.Ease(box, 0, 1, MotionKind.Fill, Show);
        else
            box.Connect(
                Node.SignalName.TreeEntered,
                Callable.From(() => Motion.Ease(box, 0, 1, MotionKind.Fill, Show)),
                (uint)GodotObject.ConnectFlags.OneShot
            );
    }

    private static void Paint(Control control)
    {
        var size = control.Size;
        var mid = size.Y / 2;
        var width = Math.Max(1, size.X - 2 * Pad);
        float X(float at) => Pad + at * width;
        var line = Look.BarFill with { A = 0.55f };
        // Whiskers: the whole range, worst to best, with caps; the line stops at the middle-half box, whose see-through
        // fill would otherwise show it running through.
        Rect2? rect = null;
        if (control.HasMeta(BoxMeta))
        {
            var q = control.GetMeta(BoxMeta).AsVector3();
            rect = new Rect2(X(q.X), mid - 5, Math.Max(2, X(q.Z) - X(q.X)), 10);
        }
        if (rect is { } inner)
        {
            control.PxRect(new Rect2(Pad, mid - 1, Math.Max(0, inner.Position.X - Pad), 2), line);
            control.PxRect(new Rect2(inner.End.X, mid - 1, Math.Max(0, Pad + width - inner.End.X), 2), line);
        }
        else
            control.PxRect(new Rect2(Pad, mid - 1, width, 2), line);
        control.PxRect(new Rect2(Pad - 1, mid - 4, 2, 8), line);
        control.PxRect(new Rect2(Pad + width - 1, mid - 4, 2, 8), line);
        if (control.HasMeta(BoxMeta) && rect is { } box)
        {
            var q = control.GetMeta(BoxMeta).AsVector3();
            control.PxRect(box, Look.BarFill with { A = 0.28f });
            control.PxRect(box, Look.BarFill with { A = 0.8f }, false, 1);
            control.PxRect(new Rect2(X(q.Y) - 1, mid - 6, 2, 12), Look.Best);
        }
        if (control.HasMeta(SpotMeta))
            Diamond(control, new Vector2(X((float)control.GetMeta(SpotMeta).AsDouble()), mid), Pad);
    }

    /// <summary>The marker for this target: a bright diamond with a dark edge.</summary>
    private static void Diamond(CanvasItem item, Vector2 centre, float radius)
    {
        Vector2[] Points(float r) =>
            [
                centre + new Vector2(0, -r),
                centre + new Vector2(r, 0),
                centre + new Vector2(0, r),
                centre + new Vector2(-r, 0),
            ];
        item.DrawColoredPolygon(Points(radius), Colors.Black with { A = 0.8f });
        item.DrawColoredPolygon(Points(radius - 1.5f), Look.BarFill);
    }

    private static Control Icon(float width, Action<Control> paint)
    {
        var icon = new Control
        {
            CustomMinimumSize = new Vector2(width, 12),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        icon.Draw += () => paint(icon);
        return icon;
    }

    // Legend icons: the diamond, the box and the median line, as drawn in the rows.
    public static Control DiamondIcon() => Icon(12, i => Diamond(i, i.Size / 2, 6));

    public static Control BoxIcon() =>
        Icon(
            18,
            i =>
            {
                var rect = new Rect2(1, 1, 16, 10);
                i.PxRect(rect, Look.BarFill with { A = 0.28f });
                i.PxRect(rect, Look.BarFill with { A = 0.8f }, false, 1);
            }
        );

    public static Control MedianIcon() => Icon(6, i => i.PxRect(new Rect2(2, 0, 2, 12), Look.Best));
}
