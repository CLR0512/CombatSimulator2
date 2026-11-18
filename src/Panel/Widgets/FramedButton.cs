using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.SetupTab;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A button for a dense view in the game's attack panel: a thin straight edge and soft shading over a solid fill,
/// which the game draws in the attack's colour. The texture is mostly see-through, so each state is the fill baked
/// under it: dark grey at rest, lighter under the pointer, green when chosen. The view's one main action stands apart
/// in an equipment slot's frame, tinted green. Textures are shrunk so their edges are thin lines, and nine-sliced so
/// they stay so at any size; a dropdown's list opens in the attack panel too.
/// </summary>
internal static class FramedButton
{
    private const string Texture = "res://Assets/UI/Textures/Combat/TEX_Combat_ItemSlot.png";

    // The texture is 372 px tall; shrunk to about a button's height its edge is a line or two thick.
    private const float Shrink = 0.1f;

    // Of the shrunk texture, the edge kept unstretched on each side.
    private const int Edge = 3;

    private static readonly Color Rest = new(0.24f, 0.24f, 0.24f);
    private static readonly Color Hover = new(0.32f, 0.32f, 0.32f);
    private static readonly Color Chosen = new(0.17f, 0.33f, 0.15f);
    private static readonly Color ChosenHover = new(0.2f, 0.39f, 0.18f);

    // The main action's tint over the slot frame's own colours.
    private static readonly Color MainTint = new(0.6f, 1f, 0.62f);

    // The game draws its slot frame, a texture about 96 px across, in slots of about 40 px.
    private const float SlotShrink = 40f / 96f;
    private const int SlotEdge = 8;
    private static readonly Color Off = new(0.15f, 0.15f, 0.15f);
    private static readonly Color ListFill = new(0.12f, 0.12f, 0.12f);

    private static readonly Dictionary<Color, Texture2D> _made = [];
    private static readonly Dictionary<Texture2D, Texture2D> _slotShrunk = [];

    /// <summary>
    /// Frames <paramref name="button"/>, <paramref name="side"/> and <paramref name="ends"/> its text's margins;
    /// <paramref name="main"/> for the view's one main action.
    /// </summary>
    public static T Make<T>(T button, float side = 8, float ends = 3, bool main = false)
        where T : Button
    {
        ArgumentNullException.ThrowIfNull(button);
        if (main)
            return Slotted(button, side, ends, MainTint, MainTint);
        button.ThemeTypeVariation = "";
        button.AddThemeStyleboxOverride("normal", Box(Rest, side, ends));
        button.AddThemeStyleboxOverride("hover", Box(Hover, side, ends));
        button.AddThemeStyleboxOverride("pressed", Box(Chosen, side, ends));
        button.AddThemeStyleboxOverride("hover_pressed", Box(ChosenHover, side, ends));
        button.AddThemeStyleboxOverride("disabled", Box(Off, side, ends));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    /// <summary>The tint of a chosen option in slot frames.</summary>
    public static readonly Color ChosenTint = new(0.5f, 0.95f, 0.55f);

    /// <summary>
    /// Frames <paramref name="button"/> in an equipment slot's frames, tinted <paramref name="tint"/> when given; a
    /// chosen one (pressed) tinted <paramref name="chosen"/>.
    /// </summary>
    public static T Slotted<T>(T button, float side, float ends, Color? tint, Color? chosen)
        where T : Button
    {
        ArgumentNullException.ThrowIfNull(button);
        button.ThemeTypeVariation = "";
        var slot = NativeStyles.Slot;
        var pressed = SlotFramed(slot.Pressed, side, ends, chosen);
        button.AddThemeStyleboxOverride("normal", SlotFramed(slot.Normal, side, ends, tint));
        button.AddThemeStyleboxOverride("hover", SlotFramed(slot.Hover, side, ends, tint));
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("hover_pressed", pressed);
        button.AddThemeStyleboxOverride("disabled", SlotFramed(slot.Disabled, side, ends, null));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return button;
    }

    // One of the slot's frames, shrunk and nine-sliced, tinted when given a tint.
    private static StyleBox SlotFramed(StyleBox box, float side, float ends, Color? tint)
    {
        var framed = (StyleBox)box.Duplicate();
        framed.ContentMarginLeft = framed.ContentMarginRight = side;
        framed.ContentMarginTop = framed.ContentMarginBottom = ends;
        if (framed is StyleBoxTexture { Texture: { } texture } textured)
        {
            textured.Texture = SlotSmall(texture);
            textured.TextureMarginLeft = textured.TextureMarginRight = SlotEdge;
            textured.TextureMarginTop = textured.TextureMarginBottom = SlotEdge;
            if (tint is { } colour)
                textured.ModulateColor = colour;
        }
        return framed;
    }

    private static Texture2D SlotSmall(Texture2D texture)
    {
        if (_slotShrunk.TryGetValue(texture, out var made) && GodotObject.IsInstanceValid(made))
            return made;
        if (texture.GetImage() is not { } image || image.IsEmpty())
            return texture;
        if (image.IsCompressed())
            image.Decompress();
        image.Resize(
            Math.Max(1, (int)(image.GetWidth() * SlotShrink)),
            Math.Max(1, (int)(image.GetHeight() * SlotShrink)),
            Image.Interpolation.Lanczos
        );
        return _slotShrunk[texture] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>The frame a framed dropdown's list opens in.</summary>
    public static StyleBox List() => Box(ListFill, 0, 0);

    private static StyleBox Box(Color fill, float side, float ends)
    {
        if (Made(fill) is not { } texture)
            return new StyleBoxFlat
            {
                BgColor = fill,
                ContentMarginLeft = side,
                ContentMarginRight = side,
                ContentMarginTop = ends,
                ContentMarginBottom = ends,
            };
        return new StyleBoxTexture
        {
            Texture = texture,
            TextureMarginLeft = Edge,
            TextureMarginRight = Edge,
            TextureMarginTop = Edge,
            TextureMarginBottom = Edge,
            ContentMarginLeft = side,
            ContentMarginRight = side,
            ContentMarginTop = ends,
            ContentMarginBottom = ends,
        };
    }

    // The game's texture shrunk once and laid over a solid fill; null when the game no longer has it, and the boxes
    // are then flat.
    private static Texture2D? Made(Color fill)
    {
        if (_made.TryGetValue(fill, out var made) && GodotObject.IsInstanceValid(made))
            return made;
        if (Art.Resource<Texture2D>(Texture)?.GetImage() is not { } image || image.IsEmpty())
            return null;
        if (image.IsCompressed())
            image.Decompress();
        image.Convert(Image.Format.Rgba8);
        image.Resize(
            Math.Max(8, (int)(image.GetWidth() * Shrink)),
            Math.Max(8, (int)(image.GetHeight() * Shrink)),
            Image.Interpolation.Lanczos
        );
        var under = Image.CreateEmpty(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8);
        under.Fill(fill);
        under.BlendRect(image, new Rect2I(0, 0, image.GetWidth(), image.GetHeight()), Vector2I.Zero);
        return _made[fill] = ImageTexture.CreateFromImage(under);
    }
}
