using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>How CS2's window is drawn for the skin shown.</summary>
public enum SkinDraw
{
    /// <summary>The game's look, from the project theme (mElvUI's skin is off, so it is the game's).</summary>
    GameTheme,

    /// <summary>The game's look kept under mElvUI's skin: the window skips the skin and uses its base theme.</summary>
    BaseThemeUnderSkip,

    /// <summary>mElvUI's look while its skin is off: CS2's own flat boxes from mElvUI's palette.</summary>
    FlatFromPalette,

    /// <summary>mElvUI's look while its skin is on: mElvUI styles the window like any of the game's.</summary>
    StyledByMElvUI,
}

/// <summary>The skin chosen, the one shown, how it is drawn, and why the two differ (null when they don't).</summary>
public readonly record struct SkinResolution(string Chosen, string Shown, SkinDraw Draw, Note? Reason)
{
    /// <summary>Whether the window draws mElvUI's look (flat), whoever draws it.</summary>
    public bool IsMElvUILook => Draw is SkinDraw.FlatFromPalette or SkinDraw.StyledByMElvUI;
}

/// <summary>
/// Which skin CS2's window shows. Base Game is always possible; mElvUI only while mElvUI is installed with a palette
/// CS2 can read. A chosen skin that can't be shown falls back to Base Game, with the reason.
/// </summary>
public static class SkinChoice
{
    public const string Base = Preferences.BaseSkin;
    public const string MElvUI = Preferences.MElvUISkin;

    /// <summary>Normalises a saved or typed skin name, reading anything unknown as <see cref="Base"/>.</summary>
    private static string Normalise(string? name) => Preferences.NormaliseSkin(name);

    /// <summary>
    /// Resolves <paramref name="chosen"/>. <paramref name="unavailable"/> is why mElvUI's look can't be drawn (null
    /// when it can); <paramref name="skinOn"/> is whether mElvUI's skin is restyling the game now.
    /// </summary>
    public static SkinResolution Resolve(string? chosen, Note? unavailable, bool skinOn)
    {
        var choice = Normalise(chosen);
        if (choice == MElvUI && unavailable == null)
            return new(choice, MElvUI, skinOn ? SkinDraw.StyledByMElvUI : SkinDraw.FlatFromPalette, null);
        var draw = skinOn ? SkinDraw.BaseThemeUnderSkip : SkinDraw.GameTheme;
        return new(choice, Base, draw, choice == Base ? null : unavailable);
    }
}
