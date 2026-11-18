using System.Runtime.CompilerServices;
using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A flat box drawn on whole screen pixels, whatever scale the window is drawn at: its edges are on pixel edges and
/// its border is a whole number of pixels thick, never less than one, so a thin border is the same on every side and
/// never drops out. As a theme box the same border is a whole number of the window's units, which rarely are whole
/// pixels. The control keeps the box's content margins, so its layout is the one the box gives as a theme box.
/// </summary>
internal static class PixelBox
{
    private const string PanelState = "panel";

    private sealed class Boxes
    {
        public readonly Dictionary<string, StyleBoxFlat> Designs = new(StringComparer.Ordinal);
        public readonly Dictionary<string, (StyleBoxFlat Box, Vector2 Scale)> InPixels = new(StringComparer.Ordinal);
        public bool PanelDrawn;
        public Control? Behind;
    }

    private static readonly ConditionalWeakTable<Control, Boxes> _boxes = [];
    private static readonly Side[] Sides = [Side.Left, Side.Top, Side.Right, Side.Bottom];
    private static readonly Corner[] Corners = [Corner.TopLeft, Corner.TopRight, Corner.BottomRight, Corner.BottomLeft];

    /// <summary>
    /// Makes <paramref name="box"/> the control's <c>panel</c>: drawn on whole pixels when it is flat, as the theme
    /// draws it otherwise (a textured box has no thin line to lose). Called again, it replaces the box.
    /// </summary>
    public static void Panel(Control control, StyleBox box)
    {
        if (!Keep(control, PanelState, box, out var boxes))
            return;
        if (boxes.PanelDrawn)
            return;
        boxes.PanelDrawn = true;
        control.Draw += () =>
        {
            if (boxes.Designs.TryGetValue(PanelState, out var design))
                Paint(control, boxes, PanelState, design);
        };
    }

    /// <summary>
    /// Makes <paramref name="box"/> the box a button or a text field shows in <paramref name="state"/> (normal,
    /// hover, pressed, hover_pressed, disabled, read_only, focus), as <see cref="Panel"/> does for a panel. The
    /// control draws its text and icon over its own box, so the box is drawn by a layer behind it.
    /// </summary>
    public static void State(Control control, string state, StyleBox box)
    {
        if (!Keep(control, state, box, out var boxes) || boxes.Behind != null)
            return;
        var behind = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ShowBehindParent = true };
        boxes.Behind = behind;
        // Not one of the control's own children to anything that counts or reads them.
        control.AddChild(behind, false, Node.InternalMode.Front);
        behind.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        behind.Draw += () =>
        {
            if (Shown(control, boxes) is { } shown)
                Paint(behind, boxes, shown, boxes.Designs[shown]);
        };
        // The control redraws when its state changes; the layer follows.
        control.Draw += behind.QueueRedraw;
    }

    /// <summary>
    /// Draws every box again. A box is drawn for the scale it was on screen at, and a control isn't asked to draw
    /// again when only the scale of what holds it changes.
    /// </summary>
    public static void Redraw()
    {
        foreach (var (control, boxes) in _boxes)
        {
            if (!GodotObject.IsInstanceValid(control))
                continue;
            control.QueueRedraw();
            if (boxes.Behind is { } behind && GodotObject.IsInstanceValid(behind))
                behind.QueueRedraw();
        }
    }

    // Takes the box for a state: a flat one is kept to draw and the control gets its margins alone; any other is
    // the control's own again. False when there is nothing for this class to draw.
    private static bool Keep(Control control, string state, StyleBox box, out Boxes boxes)
    {
        var known = _boxes.TryGetValue(control, out boxes!);
        if (box is not StyleBoxFlat flat)
        {
            if (known)
                boxes.Designs.Remove(state);
            control.AddThemeStyleboxOverride(state, box);
            control.QueueRedraw();
            return false;
        }
        if (!known)
        {
            boxes = new Boxes();
            _boxes.Add(control, boxes);
        }
        boxes.Designs[state] = flat;
        boxes.InPixels.Remove(state);
        var margins = new StyleBoxEmpty();
        foreach (var side in Sides)
            margins.SetContentMargin(side, flat.GetMargin(side));
        control.AddThemeStyleboxOverride(state, margins);
        control.QueueRedraw();
        return true;
    }

    // The state whose box shows now, among those kept: a state without one shows the nearest that has.
    private static string? Shown(Control control, Boxes boxes)
    {
        var state = control switch
        {
            BaseButton button => button.GetDrawMode() switch
            {
                BaseButton.DrawMode.Pressed => "pressed",
                BaseButton.DrawMode.Hover => "hover",
                BaseButton.DrawMode.Disabled => "disabled",
                BaseButton.DrawMode.HoverPressed => "hover_pressed",
                _ => "normal",
            },
            LineEdit { Editable: false } => "read_only",
            _ => control.HasFocus() && boxes.Designs.ContainsKey("focus") ? "focus" : "normal",
        };
        if (boxes.Designs.ContainsKey(state))
            return state;
        if (state == "hover_pressed" && boxes.Designs.ContainsKey("pressed"))
            return "pressed";
        return boxes.Designs.ContainsKey("normal") ? "normal" : null;
    }

    private static void Paint(Control on, Boxes boxes, string state, StyleBoxFlat design)
    {
        var to = on.GetViewport().GetFinalTransform() * on.GetGlobalTransformWithCanvas();
        var scale = new Vector2(to.X.Length(), to.Y.Length());
        if (scale.X <= 0 || scale.Y <= 0)
            return;
        var across = new PixelGrid(to.Origin.X, scale.X);
        var down = new PixelGrid(to.Origin.Y, scale.Y);
        if (!boxes.InPixels.TryGetValue(state, out var drawn) || drawn.Scale != scale)
            boxes.InPixels[state] = drawn = (InPixels(design, across, down), scale);
        // The box's own reach past the control (or short of it), in the window's units like the control's size.
        var left = -design.GetExpandMargin(Side.Left);
        var top = -design.GetExpandMargin(Side.Top);
        var right = on.Size.X + design.GetExpandMargin(Side.Right);
        var bottom = on.Size.Y + design.GetExpandMargin(Side.Bottom);
        var size = new Vector2(Math.Max(1, across.Between(left, right)), Math.Max(1, down.Between(top, bottom)));
        // From here one unit is one pixel, starting on a pixel's corner.
        on.DrawSetTransform(
            new Vector2((float)across.Snap(left), (float)down.Snap(top)),
            0,
            new Vector2(1 / scale.X, 1 / scale.Y)
        );
        drawn.Box.Draw(on.GetCanvasItem(), new Rect2(Vector2.Zero, size));
        on.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    // The design with every length in pixels: a border or a corner it has stays at least a pixel.
    private static StyleBoxFlat InPixels(StyleBoxFlat design, PixelGrid across, PixelGrid down)
    {
        var box = (StyleBoxFlat)design.Duplicate();
        foreach (var side in Sides)
        {
            var grid = side is Side.Left or Side.Right ? across : down;
            var width = design.GetBorderWidth(side);
            box.SetBorderWidth(side, width > 0 ? (int)grid.Pixels(width) : 0);
            box.SetExpandMargin(side, 0);
        }
        foreach (var corner in Corners)
        {
            var radius = design.GetCornerRadius(corner);
            box.SetCornerRadius(corner, radius > 0 ? (int)across.Pixels(radius) : 0);
        }
        return box;
    }
}
