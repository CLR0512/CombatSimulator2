using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The About icon in the title bar: dim until the mouse is over it. After an update it is bright, with a red dot on
/// its corner, until About is opened. Main thread.
/// </summary>
internal static class AboutButton
{
    private const float Dim = 0.4f;
    private const float IconSize = 24;
    private const float DotRadius = 4;
    private const float DotOutline = 2;

    // Brighter than any red in the palette, whose reds are for deaths: this one only says "look here".
    private static readonly Color DotColour = Color.FromHtml("#e32121");

    private static HBoxContainer? _slot;
    private static Button? _info;
    private static bool _over;

    /// <summary>Makes the button for a newly built title bar.</summary>
    public static Control Make()
    {
        Button? info = null;
        info = IconButton.Make(
            Art.File(Art.InfoPath) ?? Art.Missing,
            Tr.Get("About"),
            "Cs2About",
            () => HelpLayer.OpenAbout(info),
            bare: true
        );
        info.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // Drawn at the button's left, where Follow lines it up: centred, its edge moved with the icon's drawn size.
        info.IconAlignment = HorizontalAlignment.Left;
        info.MouseEntered += () => Hover(true);
        info.MouseExited += () => Hover(false);
        info.Draw += () => PaintDot(info);
        _info = info;
        _over = false;
        _slot = new HBoxContainer { Name = "Cs2AboutSlot" };
        _slot.AddChild(info);
        Show();
        return _slot;
    }

    /// <summary>
    /// Starts the icon at the left edge of whichever of <paramref name="panels"/> shows (a single run's details or a
    /// comparison's), so it lines up with the details panel under it at any UI scale: its slot widens leftwards to
    /// there whenever the title bar moves or a panel moves. Kept where it was while none shows.
    /// </summary>
    public static void Follow(params Control[] panels)
    {
        if (_slot is not { } slot || slot.GetParent() is not Control bar || _info is not { } info)
            return;
        void Align()
        {
            if (!GodotObject.IsInstanceValid(slot))
                return;
            if (panels.FirstOrDefault(p => GodotObject.IsInstanceValid(p) && p.IsVisibleInTree()) is not { } panel)
                return;
            var left = (bar.GetGlobalTransform().AffineInverse() * panel.GlobalPosition).X;
            // The icon starts past the button's own left margin: the icon's edge is what lines up.
            var inset = info.GetThemeStylebox("normal").GetMargin(Side.Left);
            var width = Math.Max(0, (float)Math.Round(slot.Position.X + slot.Size.X - left + inset));
            if (Math.Abs(slot.CustomMinimumSize.X - width) >= 1)
                slot.CustomMinimumSize = new Vector2(width, 0);
        }
        // The bar moves with the window and the scale; a panel moves or first shows after the bar's layout.
        bar.ItemRectChanged += Align;
        foreach (var panel in panels)
        {
            panel.ItemRectChanged += Align;
            panel.VisibilityChanged += Align;
        }
        Callable.From(Align).CallDeferred();
    }

    /// <summary>Draws the button as it is now: bright under the mouse or with the dot, dim otherwise.</summary>
    public static void Show()
    {
        if (_info == null || !GodotObject.IsInstanceValid(_info))
            return;
        _info.Modulate = new Color(1, 1, 1, _over || Usage.Dot ? 1 : Dim);
        _info.QueueRedraw();
    }

    private static void Hover(bool over)
    {
        _over = over;
        Show();
    }

    // On the icon's top right corner, ringed in the colour behind it so it reads as apart from the icon.
    private static void PaintDot(Button info)
    {
        if (!Usage.Dot)
            return;
        var box = info.GetThemeStylebox("normal");
        var side = Math.Min(IconSize, info.Size.Y - box.GetMargin(Side.Top) - box.GetMargin(Side.Bottom));
        var corner = new Vector2(box.GetMargin(Side.Left) + side, (info.Size.Y - side) / 2);
        info.DrawCircle(corner, DotRadius + DotOutline, Cs2Modal.Backdrop, antialiased: true);
        info.DrawCircle(corner, DotRadius, DotColour, antialiased: true);
    }

    public static void Reset()
    {
        _slot = null;
        _info = null;
        _over = false;
    }
}
