using Godot;
using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// mElvUI's look drawn by CS2 itself, for when mElvUI's own skin is off: flat boxes from the role recipes in mElvUI's
/// palette (fill and border per state, its border width and corner radius), with the game's content margins so the
/// layout is the same in both skins. Main thread only.
/// </summary>
internal static class FlatTheme
{
    private static readonly string[] ButtonStates = ["normal", "hover", "pressed", "hover_pressed", "disabled"];

    /// <summary>The game's theme variations CS2 uses, with the type each varies.</summary>
    public static IEnumerable<(string Variation, string Base)> VariationBases =>
        Variations.Select(v => (v.Variation, v.Base));

    // The game's theme variations CS2 uses, the type each varies and the role it draws as.
    private static readonly (string Variation, string Base, string Role)[] Variations =
    [
        (Look.Button4, "Button", "button"),
        (Look.Button8, "Button", "button"),
        (Look.Action8, "Button", "button-confirm"),
        (Look.Cancel8, "Button", "button-cancel"),
        (Look.Close, "Button", "button-cancel"),
        (Look.Tab, "Button", "tab"),
    ];

    /// <summary>The role and state each of the game's StyleBoxes CS2 draws with stands for.</summary>
    private static readonly Dictionary<string, (string Role, string State)> BoxRoles = new(StringComparer.Ordinal)
    {
        [Art.FrameBox] = ("panel", "normal"),
        [Art.SideBox] = ("panel", "normal"),
        [Art.RowBox] = ("inset", "normal"),
        [Art.RibbonBox] = ("header", "normal"),
        [Art.TabSelectedBox] = ("tab", "pressed"),
        [Art.TabUnselectedBox] = ("tab", "normal"),
        [Art.TabHoverBox] = ("tab", "hover"),
        [Art.TooltipDarkBox] = ("tooltip", "normal"),
        [Art.TooltipLightBox] = ("tooltip", "normal"),
        [Look.SlotBox] = ("slot", "normal"),
    };

    /// <summary>The flat box standing in for one of the game's StyleBoxes (by <see cref="Look"/>'s names).</summary>
    public static StyleBox? Box(MelvPalette palette, string name, StyleBox? like) =>
        BoxRoles.TryGetValue(name, out var role) ? Make(palette, role.Role, role.State, like) : null;

    /// <summary>A role's box in a state, with <paramref name="like"/>'s content margins.</summary>
    public static StyleBox Make(MelvPalette palette, string role, string state, StyleBox? like)
    {
        var style = palette.Style(role, state);
        var box = new StyleBoxFlat { AntiAliasing = false };
        if (style.Fill is { } fill)
            box.BgColor = ToColor(fill);
        else
            box.DrawCenter = false;
        if (style.Border is { } border)
        {
            box.BorderColor = ToColor(border);
            box.SetBorderWidthAll((int)Math.Round(palette.BorderWidth));
        }
        box.SetCornerRadiusAll((int)Math.Round(palette.CornerRadius));
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
            box.SetContentMargin(side, like?.GetContentMargin(side) ?? (side is Side.Left or Side.Right ? 8 : 4));
        return box;
    }

    /// <summary>
    /// The window theme for the flat look: buttons, option buttons, text fields, panels, the progress track and
    /// tooltips, and the game variations CS2 uses, all flat. <paramref name="game"/> is the game's own theme, for
    /// content margins and variation bases.
    /// </summary>
    public static Theme Build(MelvPalette palette, Theme? game)
    {
        var theme = new Theme();
        Buttons(theme, palette, game, "Button", "button");
        foreach (var (variation, type, role) in Variations)
        {
            StringName basis = game?.GetTypeVariationBase(variation) is { IsEmpty: false } b ? b : type;
            theme.SetTypeVariation(variation, basis);
            Buttons(theme, palette, game, variation, role);
        }
        foreach (var state in new[] { "normal", "focus", "read_only" })
            theme.SetStylebox(
                state,
                "LineEdit",
                Make(palette, "inset", state == "read_only" ? "disabled" : "normal", Item(game, state, "LineEdit"))
            );
        theme.SetStylebox("panel", "Panel", Make(palette, "panel", "normal", Item(game, "panel", "Panel")));
        theme.SetStylebox(
            "panel",
            "PanelContainer",
            Make(palette, "panel", "normal", Item(game, "panel", "PanelContainer"))
        );
        theme.SetStylebox(
            "background",
            "ProgressBar",
            Make(palette, "bar-track", "normal", Item(game, "background", "ProgressBar"))
        );
        theme.SetStylebox(
            "panel",
            "TooltipPanel",
            Make(palette, "tooltip", "normal", Look.GameBox(Art.TooltipDarkBox))
        );
        if (palette.Colour("text") is { } text)
        {
            theme.SetColor("font_color", "Label", ToColor(text));
            theme.SetColor("font_color", "TooltipLabel", ToColor(text));
            theme.SetColor("font_color", "LineEdit", ToColor(text));
        }
        return theme;
    }

    private static void Buttons(Theme theme, MelvPalette palette, Theme? game, string type, string role)
    {
        KeepText(theme, game, type);
        foreach (var state in ButtonStates)
            theme.SetStylebox(state, type, Make(palette, role, state, Item(game, state, type)));
        theme.SetStylebox("focus", type, new StyleBoxEmpty());
        var colours = new (string Item, string Colour)[]
        {
            ("font_color", "text"),
            ("font_hover_color", "text-bright"),
            ("font_pressed_color", "text-bright"),
            ("font_hover_pressed_color", "text-bright"),
            ("font_focus_color", "text"),
            ("font_disabled_color", "text-disabled"),
        };
        foreach (var (item, name) in colours)
            if (palette.Colour(name) is { } colour)
                theme.SetColor(item, type, ToColor(colour));
    }

    // The game's fonts and spacing for a type, so text and layout match the game look; only boxes and colours are
    // mElvUI's. Font sizes are CS2's own (the window theme's).
    private static void KeepText(Theme theme, Theme? game, string type)
    {
        if (game == null)
            return;
        foreach (var name in game.GetFontList(type))
            theme.SetFont(name, type, game.GetFont(name, type));
        foreach (var name in game.GetConstantList(type))
            theme.SetConstant(name, type, game.GetConstant(name, type));
    }

    // The game's box for a theme item, for its margins; null when the game has none.
    private static StyleBox? Item(Theme? game, string name, string type) =>
        game?.HasStylebox(name, type) == true ? game.GetStylebox(name, type) : null;

    private static Color ToColor(PaletteColour colour) => new(colour.R, colour.G, colour.B, colour.A);
}
