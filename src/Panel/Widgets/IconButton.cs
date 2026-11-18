using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A square button showing only an icon, in CS2's button look, with a tooltip saying what it does. It takes keyboard
/// focus, so focus can come back to it when something it opened closes.
/// </summary>
internal static class IconButton
{
    public const float Size = 42;
    private const float BareSize = 32;

    /// <summary>
    /// Makes the button; with <paramref name="bare"/> it shows the icon alone, without the button's frame, for a
    /// button that sits on a panel of its own (a title ribbon).
    /// </summary>
    public static Button Make(Texture2D? icon, string tooltip, string name, Action pressed, bool bare = false)
    {
        var button = Ui.Button("", bare ? "" : Look.Button8, name, pressed);
        button.Flat = bare;
        button.Icon = icon;
        button.ExpandIcon = true;
        button.IconAlignment = HorizontalAlignment.Center;
        button.VerticalIconAlignment = VerticalAlignment.Center;
        button.TooltipText = tooltip;
        button.FocusMode = Control.FocusModeEnum.All;
        button.CustomMinimumSize = bare ? new Vector2(BareSize, BareSize) : new Vector2(Size, Size);
        button.AddThemeConstantOverride("icon_max_width", 24);
        return button;
    }

    /// <summary>
    /// A bare icon, quieter than the buttons beside it until pointed at: a shortcut to what a button nearby also
    /// does, or a way into help that shouldn't compete with them.
    /// </summary>
    public static Button Quiet(Texture2D? icon, string tooltip, string name, Action pressed)
    {
        var button = Make(icon, tooltip, name, pressed, bare: true);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        foreach (
            var (state, alpha) in new[]
            {
                ("normal", 0.5f),
                ("focus", 0.5f),
                ("hover", 0.85f),
                // Held down it dims, and lights again on release, as a button sinks and rises.
                ("pressed", 0.3f),
                ("hover_pressed", 0.3f),
                ("disabled", 0.2f),
            }
        )
            button.AddThemeColorOverride($"icon_{state}_color", Colors.White with { A = alpha });
        return button;
    }
}
