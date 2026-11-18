using Godot;
using MelvorMods.CombatSimulator2.Game;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The About window's line icons, which the game has none of: CS2's own drawings in <c>data/icons/about</c>, read
/// when first shown. One that can't be read shows the game's missing-art picture. Main thread.
/// </summary>
internal static class AboutIcons
{
    public const string Code = "code";
    public const string Report = "report";
    public const string Globe = "globe";
    public const string Assisted = "assisted";
    public const string Estimate = "estimate";
    public const string Safe = "safe";
    public const string Local = "local";
    public const string Unchecked = "unchecked";

    // Drawn at 18 to 20 units and scaled with the window: twice the size stays sharp at the largest scale.
    private const float DrawnScale = 2;

    private static readonly Dictionary<string, Texture2D?> _icons = new(StringComparer.Ordinal);

    /// <summary>CS2's own picture, the one its header button shows while the window is open; the mod's to dispose.</summary>
    public static Texture2D? Mark { get; set; }

    /// <summary>The mod's own folder, set when it loads.</summary>
    public static string? Folder { get; set; }

    public static Texture2D? Get(string name)
    {
        if (_icons.TryGetValue(name, out var icon))
            return icon ?? Art.Missing;
        return (_icons[name] = Read(name)) ?? Art.Missing;
    }

    private static ImageTexture? Read(string name)
    {
        if (Folder == null)
            return null;
        var path = Path.Combine(Folder, "data", "icons", "about", name + ".svg");
        try
        {
            using var image = new Image();
            var error = image.LoadSvgFromString(File.ReadAllText(path), DrawnScale);
            if (error == Error.Ok)
                return ImageTexture.CreateFromImage(image);
            Log.Warn($"About icon {name} not drawn: {error}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"About icon {name} not read: {e.Message}");
        }
        return null;
    }

    public static void Clear()
    {
        foreach (var icon in _icons.Values)
            icon?.Dispose();
        _icons.Clear();
        Mark = null;
    }
}
