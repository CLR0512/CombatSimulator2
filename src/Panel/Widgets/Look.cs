using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// Everything CS2's panel looks like: the game's theme variations for buttons, tabs and option buttons, its shared
/// StyleBox resources for panels, ribbons and tooltips, and its font, or their flat stand-ins in mElvUI's look. Which
/// look is drawn follows the Skin setting (<see cref="Skins"/>). Colours that mean something (deaths, best, running)
/// are the same in both looks. Main thread only.
/// </summary>
internal static class Look
{
    // The game's theme variations.
    public const string Button4 = "TexturedButton4x";
    public const string Button8 = "TexturedButton8x";
    public const string Action8 = "TexturedActionButtonGreen8x";
    public const string Cancel8 = "TexturedButtonCancel8x";
    public const string Close = "TexturedButtonCancel2x";
    public const string Tab = "TexturedTabButtonLeft";

    /// <summary>CS2's own icon slot box (not a game file).</summary>
    public const string SlotBox = "cs2_slot";

    /// <summary>CS2's own box for a dropdown's list, framed as <see cref="FramedButton"/>s are.</summary>
    public const string FramedListBox = "cs2_framed_list";

    // Meaningful colours, the same in both looks.
    public static readonly Color Safe = Color.FromHtml("#4caf50");
    public static readonly Color Risky = Color.FromHtml("#ec762d");
    public static readonly Color Deadly = Color.FromHtml("#e66a6a");
    private static readonly Color SafeFill = Color.FromHtml("#22602b");
    public static readonly Color RiskyFill = Color.FromHtml("#5a2c0e");
    public static readonly Color DeadlyFill = Color.FromHtml("#6a2626");
    public static readonly Color BarFill = Color.FromHtml("#3cc4c0");

    /// <summary>
    /// Gold: the best result, and what is the player's own (pins, sides, the sort). No deaths colour is gold, so
    /// a best row beside a "some deaths" row reads at a glance.
    /// </summary>
    public static readonly Color Best = Color.FromHtml("#e0b84a");
    public static readonly Color Running = Color.FromHtml("#4a9de0");

    /// <summary>
    /// An alternative setup's own colour: its banner and what it holds other than the best. Not gold, which marks
    /// what can be clicked and the player's own.
    /// </summary>
    public static readonly Color Alternative = Color.FromHtml("#8ec5f2");

    /// <summary>The simulation engine's own colour (run progress and status), used nowhere else.</summary>
    public static readonly Color Engine = Color.FromHtml("#9b7ef0");
    public static readonly Color CheckOn = Color.FromHtml("#45a84f");

    /// <summary>
    /// Trials that ran over the limit: pinks, apart from the deaths colours, best, the bars and the engine's violet.
    /// </summary>
    public static readonly Color RanOver = Color.FromHtml("#f0a0d0");
    private static readonly Color RanOverFill = Color.FromHtml("#4f2a40");
    public static readonly Color ManyRanOver = Color.FromHtml("#c2409a");
    private static readonly Color ManyRanOverFill = Color.FromHtml("#4d1a3d");

    private static readonly Dictionary<string, StyleBox> _boxes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, StyleBox> _flatBoxes = new(StringComparer.Ordinal);
    private static readonly Dictionary<TextStyle, LabelSettings> _labels = [];
    private static readonly Dictionary<int, Font> _fonts = [];

    /// <summary>
    /// Disposes the fonts and label settings made here when the game quits, while Godot's text server still runs (a
    /// font Godot frees after it would crash the game as it closes), and forgets the boxes.
    /// </summary>
    public static void Release()
    {
        foreach (var settings in _labels.Values)
            settings.Dispose();
        foreach (var font in _fonts.Values)
            font.Dispose();
        _labels.Clear();
        _even.Clear();
        _fonts.Clear();
        _boxes.Clear();
        _flatBoxes.Clear();
    }

    /// <summary>Death rate at or above which a target is deadly; any death at all makes it risky.</summary>
    private const double DeadlyRate = 0.05;

    /// <summary>The look drawn: the game's in the Base Game skin, flat in mElvUI's (whoever draws it).</summary>
    public static LookKind Kind => Skins.Current.IsMElvUILook ? LookKind.MElvUI : LookKind.Game;

    public static bool IsGameLook => Kind == LookKind.Game;

    /// <summary>Whether CS2 draws mElvUI's look itself (mElvUI's skin is off).</summary>
    private static bool IsFlat => Skins.Current.Draw == SkinDraw.FlatFromPalette;

    /// <summary>Raised when a theme change flips the look.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Re-reads the skin shown (mElvUI's skin may have flipped); raises <see cref="Changed"/> when the look flipped.
    /// </summary>
    public static void Refresh()
    {
        var before = Kind;
        Skins.Refresh();
        if (before != Kind)
            Changed?.Invoke();
    }

    /// <summary>
    /// Forgets the shared styles, fonts and boxes so the next window build makes them for the skin shown then.
    /// mElvUI edits label settings it sees in place, so ones shown under its skin can't be reused in Base Game.
    /// </summary>
    public static void ResetStyles()
    {
        _flatBoxes.Clear();
        _labels.Clear();
        _even.Clear();
        _fonts.Clear();
        Badge.Clear();
        Compare.CompareText.Clear();
    }

    /// <summary>The box for one of the game's StyleBox names in the look drawn, or its flat stand-in.</summary>
    public static StyleBox Box(string name)
    {
        if (!IsFlat || Skins.Palette is not { } palette)
            return GameBox(name);
        if (_flatBoxes.TryGetValue(name, out var flat))
            return flat;
        return _flatBoxes[name] = FlatTheme.Box(palette, name, GameBox(name)) ?? GameBox(name);
    }

    /// <summary>One of the game's StyleBoxes; a plain dark box if the game no longer has it.</summary>
    public static StyleBox GameBox(string name)
    {
        if (_boxes.TryGetValue(name, out var box))
            return box;
        if (name == SlotBox)
            return _boxes[name] = GameSlot();
        if (name == FramedListBox)
            return _boxes[name] = FramedButton.List();
        if (name == Art.CardBox && GameCard() is { } card)
            return _boxes[name] = card;
        var path = Art.BoxFolder + name + ".tres";
        box = Art.Resource<StyleBox>(path);
        if (box == null)
        {
            Log.Warn($"style {path} missing; drawn plain");
            box = new StyleBoxFlat
            {
                BgColor = new Color(0.1f, 0.1f, 0.1f),
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 6,
                ContentMarginBottom = 6,
            };
        }
        return _boxes[name] = box;
    }

    // The game draws its ability card from a large texture shrunk to about a quarter, which is what makes its edge so
    // fine. Stretched whole over a tall box it smears; shrunk to the same quarter once and nine-sliced, its corners and
    // edges keep their look at any size and only the middle stretches.
    private static StyleBoxTexture? GameCard()
    {
        if (Art.Resource<Texture2D>(Art.CardTexture)?.GetImage() is not { } image)
            return null;
        if (image.IsCompressed())
            image.Decompress();
        image.Resize(image.GetWidth() / 4, image.GetHeight() / 4, Image.Interpolation.Lanczos);
        return new StyleBoxTexture
        {
            Texture = ImageTexture.CreateFromImage(image),
            TextureMarginLeft = 6,
            TextureMarginRight = 6,
            TextureMarginTop = 6,
            TextureMarginBottom = 6,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
    }

    // CS2's slot in the game look: a dark inset with a faint edge.
    private static StyleBoxFlat GameSlot() =>
        new()
        {
            BgColor = Colors.Black with { A = 0.45f },
            BorderColor = Colors.White with { A = 0.18f },
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            ContentMarginLeft = 3,
            ContentMarginRight = 3,
            ContentMarginTop = 3,
            ContentMarginBottom = 3,
        };

    /// <summary>
    /// Draws a text field in the dark inset the rows are drawn in: the theme's input is a brown box nothing else in
    /// the panel uses. No outline when focused, as the caret already shows it.
    /// </summary>
    public static void DarkField(LineEdit edit)
    {
        var field = (StyleBox)Box(Art.RowBox).Duplicate();
        field.ContentMarginLeft = 10;
        field.ContentMarginRight = 8;
        field.ContentMarginTop = 4;
        field.ContentMarginBottom = 4;
        edit.AddThemeStyleboxOverride("normal", field);
        edit.AddThemeStyleboxOverride("read_only", field);
        edit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    /// <summary>A container drawn with one of the game's StyleBoxes (or its flat stand-in).</summary>
    public static PanelContainer Panel(string box, string name = "")
    {
        var panel = new PanelContainer();
        if (name.Length > 0)
            panel.Name = name;
        PixelBox.Panel(panel, Box(box));
        return panel;
    }

    /// <summary>
    /// Runs <paramref name="apply"/> on every theme change of <paramref name="control"/>, never from inside
    /// itself. Setting an override raises the change again, and what was set can't be compared with what is read
    /// back to stop that: a skin may put its own box, colour or size in place of the one set, so the two never
    /// match and the restyle would call itself without end.
    /// </summary>
    public static void OnTheme(Control control, Action apply)
    {
        var applying = false;
        control.ThemeChanged += () =>
        {
            if (applying)
                return;
            applying = true;
            try
            {
                apply();
            }
            finally
            {
                applying = false;
            }
        };
    }

    /// <summary>
    /// A list row's box with an outline, for the row selected or the best: an outline and a faint wash only, so the
    /// row's own colours still read. It keeps the plain row's margins, so marking a row moves nothing in it.
    /// </summary>
    public static StyleBox RowOutline(Color edge, float wash)
    {
        var plain = Box(Art.RowBox);
        var outline = new StyleBoxFlat { BgColor = Colors.White with { A = wash }, BorderColor = edge };
        outline.SetBorderWidthAll(2);
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
            outline.SetContentMargin(side, plain.GetMargin(side));
        return outline;
    }

    /// <summary>
    /// Narrows a button's side margins to <paramref name="side"/>, and its top and bottom ones to
    /// <paramref name="ends"/> when given, for short labels in a row of choices. Re-read whenever the theme changes, so
    /// it follows the skin.
    /// </summary>
    public static Button Narrow(Button button, float side, float? ends = null)
    {
        var applying = false;
        void Apply()
        {
            // Setting an override raises the theme change again.
            if (applying)
                return;
            applying = true;
            var type = button.ThemeTypeVariation;
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
                if (button.HasThemeStylebox(state, type))
                {
                    var box = (StyleBox)button.GetThemeStylebox(state, type).Duplicate();
                    box.ContentMarginLeft = side;
                    box.ContentMarginRight = side;
                    if (ends is { } e)
                        box.ContentMarginTop = box.ContentMarginBottom = e;
                    button.AddThemeStyleboxOverride(state, box);
                }
            applying = false;
        }
        button.ThemeChanged += Apply;
        return button;
    }

    /// <summary>
    /// A small action button in green: the small textured button (<see cref="Button4"/>) with its texture tinted, as the
    /// game's own green button comes only large. Re-read whenever the theme changes, so it follows the skin.
    /// </summary>
    public static Button SmallAction(Button button)
    {
        button.ThemeTypeVariation = Button4;
        var applying = false;
        void Apply()
        {
            if (applying)
                return;
            applying = true;
            foreach (
                var (state, tint) in new[]
                {
                    ("normal", ActionTint),
                    ("focus", ActionTint),
                    ("pressed", ActionTint.Darkened(0.15f)),
                    ("hover", ActionTint.Lightened(0.15f)),
                    ("hover_pressed", ActionTint.Lightened(0.15f)),
                }
            )
                if (button.HasThemeStylebox(state, Button4))
                {
                    var box = (StyleBox)button.GetThemeStylebox(state, Button4).Duplicate();
                    if (box is StyleBoxTexture texture)
                        texture.ModulateColor = tint;
                    else if (box is StyleBoxFlat flat)
                        flat.BgColor = flat.BgColor.Lerp(tint, 0.5f);
                    button.AddThemeStyleboxOverride(state, box);
                }
            applying = false;
        }
        button.ThemeChanged += Apply;
        return button;
    }

    // The green the small action button's texture is tinted with.
    private static readonly Color ActionTint = new(0.45f, 0.85f, 0.4f);

    // Text, read from the theme at draw time so a skin's colours apply.
    public static Color Text(Control control) => control.GetThemeColor("font_color", "Label");

    public static Color Muted(Control control) => Text(control) with { A = 0.6f };

    /// <summary>The game's progress bar track: the inset every bar and chart sits on.</summary>
    public static StyleBox Track(Control control) => control.GetThemeStylebox("background", "ProgressBar");

    public static Color Status(double deathRate) =>
        deathRate <= 0 ? Safe
        : deathRate < DeadlyRate ? Risky
        : Deadly;

    /// <summary>The ran-over chip's colour: the deaths badge's threshold splits some from many.</summary>
    public static Color RanOverColour(double share) => share < DeadlyRate ? RanOver : ManyRanOver;

    public static Color RanOverBack(double share) => share < DeadlyRate ? RanOverFill : ManyRanOverFill;

    /// <summary>The ran-over band's word for the bridge: "some" below the deadly rate, "many" at or above.</summary>
    public static string RanOverBand(double share) => share < DeadlyRate ? "some" : "many";

    public static Color StatusFill(double deathRate) =>
        deathRate <= 0 ? SafeFill
        : deathRate < DeadlyRate ? RiskyFill
        : DeadlyFill;

    /// <summary>
    /// The game's font at a weight (400 regular … 700 bold): from the game's own theme, which mElvUI publishes while
    /// its skin has replaced the project theme's fonts.
    /// </summary>
    public static Font Font(int weight)
    {
        if (_fonts.TryGetValue(weight, out var font))
            return font;
        var basis = Skins.BaseTheme?.DefaultFont ?? ThemeDB.GetProjectTheme()?.DefaultFont ?? ThemeDB.FallbackFont;
        var variation = new FontVariation { BaseFont = basis is FontVariation v ? v.BaseFont ?? basis : basis };
        var tags = new Godot.Collections.Dictionary
        {
            [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = weight,
        };
        variation.VariationOpentype = tags;
        return _fonts[weight] = variation;
    }

    /// <summary>
    /// A text style's settings with every digit one width, for numbers that change in place; the style as it is when
    /// the font has no such digits.
    /// </summary>
    public static LabelSettings EvenDigits(TextStyle style)
    {
        if (_even.TryGetValue(style, out var even))
            return even;
        var plain = Style(style);
        even = new LabelSettings();
        MarkSkipped(even);
        even.Font = plain.Font is FontVariation variation
            ? new FontVariation
            {
                BaseFont = variation.BaseFont,
                VariationOpentype = variation.VariationOpentype,
                OpentypeFeatures = new Godot.Collections.Dictionary
                {
                    [TextServerManager.GetPrimaryInterface().NameToTag("tnum")] = 1,
                },
            }
            : plain.Font;
        even.FontSize = plain.FontSize;
        even.FontColor = plain.FontColor;
        return _even[style] = even;
    }

    private static readonly Dictionary<TextStyle, LabelSettings> _even = [];

    /// <summary>
    /// Label settings for a text style, shared by every label in that style. In the game look they are marked for
    /// mElvUI's skin to leave alone, as they have no node for the window's skip group to reach.
    /// </summary>
    public static LabelSettings Style(TextStyle style)
    {
        if (_labels.TryGetValue(style, out var settings))
            return settings;
        var (size, weight, colour) = style switch
        {
            TextStyle.Title => (22, 700, Color.FromHtml("#f2f2f2")),
            TextStyle.Heading => (18, 700, Color.FromHtml("#f2f2f2")),
            TextStyle.BodyStrong => (17, 600, Color.FromHtml("#f2f2f2")),
            TextStyle.Body => (16, 400, Color.FromHtml("#dadada")),
            TextStyle.Value => (20, 700, Color.FromHtml("#f2f2f2")),
            TextStyle.ValueBest => (20, 700, Best),
            TextStyle.Label => (14, 500, Color.FromHtml("#dadada")),
            TextStyle.Small => (13, 400, Color.FromHtml("#a8a39b")),
            TextStyle.Tiny => (12, 400, Color.FromHtml("#a8a39b")),
            TextStyle.Gold => (14, 600, Best),
            _ => (16, 400, Color.FromHtml("#dadada")),
        };
        // Marked before anything is set: mElvUI's skin maps a colour or size set on unmarked settings.
        settings = new LabelSettings();
        MarkSkipped(settings);
        settings.Font = Font(weight);
        settings.FontSize = size;
        settings.FontColor = colour;
        return _labels[style] = settings;
    }

    /// <summary>Marks label settings for mElvUI's skin to leave alone while the game look is drawn.</summary>
    public static void MarkSkipped(LabelSettings settings)
    {
        if (IsGameLook)
            settings.SetMeta(Skins.SkipMeta, true);
        else if (settings.HasMeta(Skins.SkipMeta))
            settings.RemoveMeta(Skins.SkipMeta);
    }

    public static void Clear()
    {
        _boxes.Clear();
        _flatBoxes.Clear();
        _labels.Clear();
        _even.Clear();
        _fonts.Clear();
        Skins.Clear();
        Changed = null;
    }
}
