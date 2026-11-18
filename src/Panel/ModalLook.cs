using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Panel;

// The window's look: following the skin shown, and its theme.
internal static partial class Cs2Modal
{
    /// <summary>
    /// Sets the skin (<c>base</c> or <c>melvui</c>), saved, as the Settings menu does; the window follows on the next
    /// frame. A skin that can't be shown is still saved, and Base Game is shown with the reason.
    /// </summary>
    public static SkinResolution SetSkin(string skin)
    {
        PanelState.Prefs.Skin = skin;
        PanelState.Save();
        Skins.Refresh(findAgain: true);
        return Skins.Current;
    }

    /// <summary>
    /// Makes the window again after a language change, with every text looked up anew, as a skin change does. The
    /// panel's state (view, selection, pins) and any run in progress are kept.
    /// </summary>
    public static void Retext() => Remake("language changed");

    private static void Remake(string why)
    {
        if (_root == null || !GodotObject.IsInstanceValid(_root) || _modal == null)
            return;
        Log.Info(why + ", rebuilding the window");
        _rebuilding = true;
        Forget();
        _modal.Rebuild();
    }

    // The window follows the skin shown: between the game look with and without mElvUI's skin only the window theme
    // changes (CS2's nodes skip the skin either way); any other change makes the content again, since mElvUI styles
    // nodes as they enter the tree and undoes that only when its skin goes off.
    private static void FollowSkin()
    {
        if (_root == null || !GodotObject.IsInstanceValid(_root) || _modal == null)
            return;
        var draw = Skins.Current.Draw;
        if (draw == _builtDraw)
            return;
        if (IsGame(draw) && IsGame(_builtDraw))
        {
            _builtDraw = draw;
            RefreshTheme();
            return;
        }
        Log.Info($"skin: {_builtDraw} → {draw}, rebuilding the window");
        _rebuilding = true;
        Forget();
        _modal.Rebuild();
    }

    private static bool IsGame(SkinDraw draw) => draw is SkinDraw.GameTheme or SkinDraw.BaseThemeUnderSkip;

    private static void RefreshTheme()
    {
        if (_root != null && GodotObject.IsInstanceValid(_root))
            _root.Theme = WindowTheme();
    }

    // The control types whose font sizes CS2's window sets from the game's theme.
    private static readonly string[] _sizedTypes = ["Button", "LineEdit", "TooltipLabel"];

    // The window's theme in each skin: CS2's items alone where the project theme gives the look (the game's, or
    // mElvUI's while its skin is on); over a copy of the game's own theme while mElvUI's skin has replaced the
    // project theme and CS2 keeps the game look; over CS2's flat theme for mElvUI's look while its skin is off.
    internal static Theme WindowTheme()
    {
        var items = Cs2Items();
        Theme? under = Skins.Current.Draw switch
        {
            SkinDraw.BaseThemeUnderSkip => Skins.BaseTheme?.Duplicate() as Theme,
            SkinDraw.FlatFromPalette when Skins.Palette is { } palette => FlatTheme.Build(
                palette,
                ThemeDB.GetProjectTheme()
            ),
            _ => null,
        };
        if (under == null)
            return items;
        under.MergeWith(items);
        return under;
    }

    // Plain tooltips get the game's own tooltip box: the project theme gives them none, as the game draws its
    // tooltips itself. A theme on the window's root reaches everything inside it, tooltips included.
    private static Theme Cs2Items()
    {
        var theme = new Theme();
        theme.SetStylebox("panel", "TooltipPanel", Look.Box(Art.TooltipDarkBox));
        theme.SetColor("font_color", "TooltipLabel", Color.FromHtml("#dadada"));
        // Sizes from the game's own theme: mElvUI's skin may have resized the project theme's.
        var project = Skins.BaseTheme ?? ThemeDB.GetProjectTheme();
        foreach (var type in _sizedTypes)
        {
            var size = project?.HasFontSize("font_size", type) == true ? project.GetFontSize("font_size", type) : 0;
            // Tooltips read at CS2's body size; the game's own tooltip text is smaller.
            if (size <= 0 || type == "TooltipLabel")
                size = 16;
            theme.SetFontSize("font_size", type, size);
        }
        // The game's variations CS2 uses get their sizes here too, each from the variation's own size or its base
        // type's, so they read the same whichever theme the lookup meets first.
        foreach (var (variation, basis) in FlatTheme.VariationBases)
            theme.SetFontSize(
                "font_size",
                variation,
                project?.HasFontSize("font_size", variation) == true
                    ? project.GetFontSize("font_size", variation)
                    : theme.GetFontSize("font_size", basis)
            );
        // The tooltip's label may take its size from Label or the theme's default; CS2's own labels all set
        // theirs through LabelSettings, so these reach only tooltips and unstyled text.
        theme.SetFontSize("font_size", "Label", 16);
        theme.DefaultFontSize = 16;
        return theme;
    }
}
