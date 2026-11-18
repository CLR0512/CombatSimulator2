using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// The game's icons are drawn at 64 px or more; a copy shrunk to the text's height keeps them sharp in a row. Each
/// size of each icon is made once.
/// </summary>
internal static class ShrunkIcon
{
    private static readonly Dictionary<(Texture2D, int), Texture2D> Shrunk = [];

    /// <summary>An empty picture <paramref name="size"/> square: a placeholder texture would draw a checkerboard.</summary>
    public static Texture2D Blank(int size) =>
        ImageTexture.CreateFromImage(Image.CreateEmpty(size, size, false, Image.Format.Rgba8));

    public static Texture2D Of(Texture2D texture, int size)
    {
        if (Shrunk.TryGetValue((texture, size), out var made) && GodotObject.IsInstanceValid(made))
            return made;
        if (texture.GetImage() is not { } image || image.IsEmpty() || image.GetHeight() <= size)
            return texture;
        if (image.IsCompressed())
            image.Decompress();
        image.Resize(Math.Max(1, size * image.GetWidth() / image.GetHeight()), size, Image.Interpolation.Lanczos);
        return Shrunk[(texture, size)] = ImageTexture.CreateFromImage(image);
    }
}
