using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

internal sealed partial class Dropdown
{
    // The filter buttons' margins and tab box. The game shrinks its whole texture into each button, keeping it sharp;
    // this wider bar gets a copy shrunk to its height with fixed ends (notch included), and only the middle widens.
    private const int EdgeWidth = 36;
    private static readonly Dictionary<(Texture2D, int), Texture2D> Squashed = [];
    private static readonly Dictionary<Texture2D, Texture2D> Unsquashed = [];
    private bool _edging;
    private int _edgedAt = -1;

    // Setting the boxes raises the theme change again, which finds them made; each is made from the game's texture
    // again, so a new height or skin starts from the original.
    private void Edges()
    {
        if (_edging)
            return;
        _edging = true;
        var height = _edgedAt = (int)Math.Round(_bar.Size.Y);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            if (!_bar.HasThemeStylebox(state, Look.Tab))
                continue;
            var box = (StyleBox)_bar.GetThemeStylebox(state, Look.Tab).Duplicate();
            box.ContentMarginLeft = box.ContentMarginRight = 10;
            if (Ends is { } ends)
                box.ContentMarginTop = box.ContentMarginBottom = ends;
            if (box is StyleBoxTexture { Texture: { } texture } textured && height > 0)
            {
                textured.Texture = Squash(Unsquashed.GetValueOrDefault(texture, texture), height);
                textured.TextureMarginLeft = textured.TextureMarginRight = EdgeWidth;
            }
            _bar.AddThemeStyleboxOverride(state, box);
        }
        _edging = false;
    }

    private static Texture2D Squash(Texture2D texture, int height)
    {
        if (Squashed.TryGetValue((texture, height), out var made) && GodotObject.IsInstanceValid(made))
            return made;
        if (texture.GetImage() is not { } image || image.IsEmpty() || image.GetHeight() <= height)
            return texture;
        if (image.IsCompressed())
            image.Decompress();
        image.Resize(image.GetWidth(), height, Image.Interpolation.Lanczos);
        var squashed = Squashed[(texture, height)] = ImageTexture.CreateFromImage(image);
        Unsquashed[squashed] = texture;
        return squashed;
    }
}
