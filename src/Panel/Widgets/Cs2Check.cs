using Godot;
using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// CS2's checkbox: a toggle button showing the game's tick-box icons in the Game look, and a filled or empty square
/// under a skin (which never changes icons). The look is re-read whenever the theme changes.
/// </summary>
internal static class Cs2Check
{
    /// <summary>The tick box's side, which a box lined up under something else is centred by.</summary>
    public const float Box = 20;
    private static ImageTexture? _blank;
    private static ImageTexture? _gameBox;
    private static bool _gameBoxTried;

    private const string SizeMeta = "cs2_box";

    /// <summary>
    /// Draws the box at <paramref name="size"/> in place of <see cref="Box"/>, its label in the small text: a quiet
    /// tick beside larger content.
    /// </summary>
    public static void Small(Button button, float size)
    {
        button.SetMeta(SizeMeta, size);
        button.AddThemeConstantOverride("icon_max_width", (int)size);
        button.AddThemeConstantOverride("h_separation", 6);
        void Text() => button.AddThemeFontSizeOverride("font_size", Look.Style(TextStyle.Small).FontSize);
        Look.OnTheme(button, Text);
        Text();
        button.QueueRedraw();
    }

    private static float SizeOf(Button button) => (float)button.GetMeta(SizeMeta, Box).AsDouble();

    /// <summary>Disposes the icons made here when the game quits.</summary>
    public static void Release()
    {
        _blank?.Dispose();
        _blank = null;
        _gameBox?.Dispose();
        _gameBox = null;
        _gameBoxTried = false;
    }

    private static readonly Color GameTick = Color.FromHtml("#66ff99");

    public static Button Make(string name, string text = "", bool on = false, Action<bool>? toggled = null)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            Flat = true,
            ExpandIcon = false,
            FocusMode = Control.FocusModeEnum.None,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            Alignment = HorizontalAlignment.Left,
            // The game's box is drawn far smaller than its image, at any panel size: sampled from mipmaps its thin
            // border stays whole instead of dropping out where it falls between pixels.
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        };
        if (name.Length > 0)
            button.Name = name;
        button.AddThemeConstantOverride("icon_max_width", (int)Box);
        button.AddThemeConstantOverride("h_separation", 8);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        button.SetPressedNoSignal(on);
        Restyle(button);
        button.Toggled += _ => Restyle(button);
        button.ThemeChanged += () => Restyle(button);
        button.Draw += () => Paint(button);
        if (toggled != null)
            button.Toggled += v => toggled(v);
        return button;
    }

    /// <summary>Sets the state without raising Toggled, and redraws.</summary>
    public static void Set(Button button, bool on)
    {
        button.SetPressedNoSignal(on);
        Restyle(button);
    }

    private const string OwnedMeta = "cs2_owned";

    // A tick the character has and the side can't take off: plain, where a tick the player can change is green.
    private static readonly Color OwnedTick = Color.FromHtml("#b9b4a8");

    /// <summary>Marks a tick as the character's own, which can't be taken off; it draws plain instead of green.</summary>
    public static void SetOwned(Button button, bool owned)
    {
        if (button.GetMeta(OwnedMeta, false).AsBool() == owned)
            return;
        button.SetMeta(OwnedMeta, owned);
        button.QueueRedraw();
    }

    private static void Restyle(Button button)
    {
        if (!GodotObject.IsInstanceValid(button))
            return;
        Look.Refresh();
        // Game look: the game's empty box, with the tick drawn over it (the game's tick image, shrunk this far,
        // leaves a dark fringe over the box's border). Skinned: CS2's own square.
        button.Icon = Look.IsGameLook ? GameBox() ?? Blank() : Blank();
        button.QueueRedraw();
    }

    // The game's empty tick box with mipmaps (its own texture has none), made once.
    private static ImageTexture? GameBox()
    {
        if (_gameBoxTried)
            return _gameBox;
        _gameBoxTried = true;
        var image = Art.File(Art.TickOffPath)?.GetImage();
        if (image == null)
            return null;
        if (image.IsCompressed())
            image.Decompress();
        image.GenerateMipmaps();
        _gameBox = ImageTexture.CreateFromImage(image);
        return _gameBox;
    }

    private static Texture2D Blank() =>
        _blank ??= ImageTexture.CreateFromImage(Image.CreateEmpty((int)Box, (int)Box, false, Image.Format.Rgba8));

    // The skin's squares, where the icon sits.
    private static void Paint(Button button)
    {
        var box = SizeOf(button);
        var rect = new Rect2(0, (button.Size.Y - box) / 2, box, box).Grow(-1);
        // The tick's points were drawn for the standard box.
        var scale = box / Box;
        var owned = button.GetMeta(OwnedMeta, false).AsBool();
        // An owned tick is disabled but still says something: it keeps its strength.
        var dim = button.Disabled && !owned ? 0.4f : 1f;
        if (Look.IsGameLook && Art.File(Art.TickOffPath) != null)
        {
            // The game's tick: its green, a thick stroke that overshoots the box's top right as the game's does.
            if (button.ButtonPressed)
                button.DrawPolyline(
                    [
                        rect.Position + new Vector2(3, 9) * scale,
                        rect.Position + new Vector2(8, 14.5f) * scale,
                        rect.Position + new Vector2(18.5f, 1) * scale,
                    ],
                    (owned ? OwnedTick : GameTick) with
                    {
                        A = dim,
                    },
                    3.4f * scale,
                    true
                );
            return;
        }
        // Skinned: the filled square alone means on; a tick over it would say the same thing twice.
        if (button.ButtonPressed)
            button.PxRect(rect, (owned ? OwnedTick : Look.CheckOn) with { A = dim });
        else
        {
            button.PxRect(rect, Look.Muted(button) with { A = 0.55f * dim }, false, 1.5f);
        }
    }
}
