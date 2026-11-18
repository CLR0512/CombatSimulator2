using Godot;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>The game's own look for the editor's slots and column titles.</summary>
internal static class GameSlot
{
    /// <summary>A column's title between two lines, as the game draws its section titles.</summary>
    public static void Title(VBoxContainer column, string text, string name, Control? after = null)
    {
        var row = Ui.Add(column, Ui.Row(8, name + "Title"));
        row.AddChild(Line());
        var label = new Label
        {
            Text = text,
            LabelSettings = NativeStyles.TitleSettings,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        if (label.LabelSettings == null)
            label.LabelSettings = Look.Style(TextStyle.Label);
        row.AddChild(label);
        if (after != null)
            row.AddChild(after);
        row.AddChild(Line());
    }

    /// <summary>The game's title line, or a plain one when it has none.</summary>
    public static Control Line()
    {
        if (NativeStyles.TitleLine is { } texture)
            return new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                CustomMinimumSize = new Vector2(0, 2),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
        return new ColorRect
        {
            Color = Color.FromHtml("#303030"),
            CustomMinimumSize = new Vector2(0, 2),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>
    /// One of the game's slots: its frames, the icon (dimmed when empty), green-edged when changed and red-edged when
    /// its item can't be worn (<paramref name="unwearable"/>); disabled when <paramref name="readOnly"/> or it has no
    /// action.
    /// </summary>
    public static Button Make(
        string name,
        int size,
        SlotStyles styles,
        Texture2D? icon,
        bool empty,
        bool changed,
        Action? pressed,
        string tooltip,
        bool readOnly,
        (string Hint, Action Act)? rightClick = null,
        bool unwearable = false
    )
    {
        // A slot a right-click does something to says what.
        if (rightClick != null && !readOnly)
            tooltip += "\n" + Model.Marks.Muted(rightClick.Value.Hint);
        var button = new Button
        {
            Name = name,
            CustomMinimumSize = new Vector2(size, size),
            Icon = icon,
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        PixelBox.State(button, "normal", styles.Normal);
        PixelBox.State(button, "hover", styles.Hover);
        PixelBox.State(button, "pressed", styles.Pressed);
        PixelBox.State(button, "hover_pressed", styles.Pressed);
        PixelBox.State(button, "disabled", styles.Disabled);
        // A changed slot: a green outline over the game's frame, whatever kind of box that is; red over it when the
        // item can't be worn, which matters more than that it changed.
        if (changed || unwearable)
        {
            var outline = new Godot.Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            outline.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            PixelBox.Panel(outline, Outline(unwearable ? Look.Deadly : Look.Safe, 2));
            button.AddChild(outline);
        }
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeColorOverride("icon_normal_color", empty ? new Color(1, 1, 1, 0.35f) : Colors.White);
        button.AddThemeColorOverride(
            "icon_disabled_color",
            empty ? new Color(1, 1, 1, 0.25f) : new Color(1, 1, 1, 0.6f)
        );
        if (pressed != null)
            button.Pressed += pressed;
        if (rightClick is { } right)
            button.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && !button.Disabled)
                {
                    button.AcceptEvent();
                    right.Act();
                }
            };
        button.Disabled = readOnly || pressed == null;
        return button;
    }

    /// <summary>A box with only a border.</summary>
    private static StyleBoxFlat Outline(Color colour, int width)
    {
        var box = new StyleBoxFlat { DrawCenter = false, BorderColor = colour };
        box.SetBorderWidthAll(width);
        return box;
    }
}
