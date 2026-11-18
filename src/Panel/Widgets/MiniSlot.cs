using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A small gear slot for a setup card: the slot's frame around the item's icon, the slot's own art dimmed when it
/// is empty, and a gold edge when the side's changes put the item there.
/// </summary>
internal static class MiniSlot
{
    /// <summary>The smallest room a slot takes in a grid: its box, and its inset of nothing around it.</summary>
    public const float Size = 32;

    /// <summary>The largest: two cards and their key stats still fit the Setup column at it.</summary>
    public const float MaxSize = 44;

    /// <summary>
    /// The space around a slot's box, inside the slot: slots stand edge to edge in their grid, so the pointer is
    /// always on one and a tooltip doesn't drop out between two. A large slot keeps a wider gap, as its box does.
    /// </summary>
    public static float Inset(float size) => size >= 48 ? 2 : 1;

    private static readonly Color ChangedEdge = Look.Best;

    public static PanelContainer Make(
        string name,
        Texture2D? icon,
        bool empty,
        bool changed,
        string tooltip,
        Action? pressed = null,
        Action? rightPressed = null,
        float size = Size
    )
    {
        var inset = Inset(size);
        var slot = new PanelContainer
        {
            CustomMinimumSize = new Vector2(size, size),
            TooltipText = tooltip,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        if (name.Length > 0)
            slot.Name = name;
        var frame = Inside(Look.Box(Look.SlotBox), inset);
        PixelBox.Panel(slot, frame);
        // The art keeps the share of the box the game gives it: 24 of a 32 slot.
        var picture = Ui.Add(slot, Ui.Picture(icon, size - 2 * inset - Mathf.Round(size * 3 / 16)));
        picture.Modulate = empty ? new Color(1, 1, 1, 0.3f) : Colors.White;
        if (changed)
        {
            var outline = new Godot.Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            var box = new StyleBoxFlat { DrawCenter = false, BorderColor = ChangedEdge };
            box.SetBorderWidthAll(1);
            // The slot lays the edge out inside its frame's content margins; grown back out by them, it lies on
            // the frame's own border instead of a ring inside it.
            box.ExpandMarginLeft = frame.GetMargin(Side.Left) - inset;
            box.ExpandMarginTop = frame.GetMargin(Side.Top) - inset;
            box.ExpandMarginRight = frame.GetMargin(Side.Right) - inset;
            box.ExpandMarginBottom = frame.GetMargin(Side.Bottom) - inset;
            PixelBox.Panel(outline, box);
            slot.AddChild(outline);
        }
        if (pressed != null)
        {
            slot.MouseFilter = Control.MouseFilterEnum.Stop;
            slot.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            slot.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    slot.AcceptEvent();
                    pressed();
                }
                else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                {
                    slot.AcceptEvent();
                    rightPressed?.Invoke();
                }
            };
        }
        return slot;
    }

    // The box drawn short of the slot's edges by the inset.
    private static StyleBox Inside(StyleBox box, float inset)
    {
        var inside = (StyleBox)box.Duplicate();
        if (inside is StyleBoxFlat flat)
            flat.SetExpandMarginAll(-inset);
        else if (inside is StyleBoxTexture texture)
            texture.SetExpandMarginAll(-inset);
        return inside;
    }
}
