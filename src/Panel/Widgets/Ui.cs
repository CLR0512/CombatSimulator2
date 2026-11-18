using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>Plain controls in CS2's styles. Sizes are base units; every style comes from <see cref="Look"/>.</summary>
internal static class Ui
{
    /// <summary>
    /// A node name for one item, such as <c>Cs2Target_GolbinForest_Golbin</c>: the prefix, then the item's short key
    /// with <c>/</c> and <c>:</c> (not allowed in node names) as <c>_</c>.
    /// </summary>
    public static string Id(string prefix, string key) =>
        prefix + "_" + Model.Ids.BriefKey(key).Replace('/', '_').Replace(':', '_');

    public static Label Label(string text, TextStyle style = TextStyle.Body, string name = "", bool wrap = false)
    {
        var label = new Label
        {
            Text = text,
            LabelSettings = Look.Style(style),
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (wrap)
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (name.Length > 0)
            label.Name = name;
        return label;
    }

    /// <summary>A label that shortens with "…" and shows its whole text as a tooltip.</summary>
    /// <summary>
    /// A name and its values on one line, the values at its end; when both don't fit (a narrow column, a long value)
    /// the values go under the name rather than cut it short.
    /// </summary>
    public static HFlowContainer NameLine(Control name, BoxContainer values)
    {
        var line = new HFlowContainer();
        line.AddThemeConstantOverride("h_separation", 8);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(name);
        values.Alignment = BoxContainer.AlignmentMode.End;
        values.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(values);
        return line;
    }

    public static Label Clipped(string text, TextStyle style = TextStyle.Body, string name = "")
    {
        var label = Label(text, style, name);
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.ClipText = true;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.CustomMinimumSize = new Vector2(24, 0);
        label.TooltipText = text;
        label.MouseFilter = Control.MouseFilterEnum.Pass;
        return label;
    }

    public static void SetClipped(Label label, string text)
    {
        label.Text = text;
        label.TooltipText = string.IsNullOrWhiteSpace(text) ? "" : text;
    }

    public static Button Button(string text, string variation, string name, Action? pressed = null)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            ThemeTypeVariation = variation,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            FocusMode = Control.FocusModeEnum.None,
        };
        if (pressed != null)
            button.Pressed += pressed;
        return button;
    }

    /// <summary>The game's own close button: a small red square with an X, as on its menus.</summary>
    public static Button CloseButton(string name, Action pressed)
    {
        var button = Button("X", Look.Close, name, pressed);
        button.CustomMinimumSize = new Vector2(32, 32);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        button.AddThemeFontSizeOverride("font_size", 20);
        return button;
    }

    /// <summary>
    /// Draws <paramref name="control"/> out past its slot by negative margins; the slot shrinks by what overhangs.
    /// Reaching up moves the control up unless it grows <paramref name="taller"/> instead.
    /// </summary>
    public static MarginContainer Overhang(
        Control control,
        int left = 0,
        int right = 0,
        int up = 0,
        bool taller = false
    )
    {
        var margins = new MarginContainer { Name = control.Name + "Overhang" };
        margins.AddThemeConstantOverride("margin_left", -left);
        margins.AddThemeConstantOverride("margin_right", -right);
        margins.AddThemeConstantOverride("margin_top", -up);
        margins.AddThemeConstantOverride("margin_bottom", taller ? 0 : up);
        margins.AddChild(control);
        return margins;
    }

    public static HBoxContainer Row(int separation = 8, string name = "")
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", separation);
        if (name.Length > 0)
            row.Name = name;
        return row;
    }

    public static VBoxContainer Column(int separation = 6, string name = "")
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", separation);
        if (name.Length > 0)
            column.Name = name;
        return column;
    }

    public static T Add<T>(Node parent, T child)
        where T : Node
    {
        parent.AddChild(child);
        return child;
    }

    public static Control Spacer(bool expand = true, float width = 0) =>
        new()
        {
            SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
            CustomMinimumSize = new Vector2(width, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

    /// <summary>
    /// A grey copy of <paramref name="texture"/>, by its light, for an icon that should not draw the eye with colour;
    /// null for none.
    /// </summary>
    public static Texture2D? Greyscale(Texture2D? texture)
    {
        if (texture?.GetImage() is not { } image || image.IsEmpty())
            return texture;
        if (image.IsCompressed())
            image.Decompress();
        image.Convert(Image.Format.Rgba8);
        for (var y = 0; y < image.GetHeight(); y++)
        for (var x = 0; x < image.GetWidth(); x++)
        {
            var colour = image.GetPixel(x, y);
            var light = colour.R * 0.299f + colour.G * 0.587f + colour.B * 0.114f;
            image.SetPixel(x, y, new Color(light, light, light, colour.A));
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Holds <paramref name="label"/> as wide as <paramref name="widest"/>, the longest text it shows, so a row it
    /// leads doesn't move as its text changes. Called again whenever the text is set.
    /// </summary>
    public static void HoldWidth(Label label, string widest)
    {
        var font = label.GetThemeFont("font");
        var size = label.LabelSettings?.FontSize ?? label.GetThemeFontSize("font_size");
        label.CustomMinimumSize = new Vector2(Mathf.Ceil(font.GetStringSize(widest, fontSize: size).X), 0);
    }

    public static TextureRect Picture(Texture2D? texture, float size, string name = "")
    {
        var rect = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        if (name.Length > 0)
            rect.Name = name;
        return rect;
    }

    public static MarginContainer Margin(Control child, int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        margin.AddChild(child);
        return margin;
    }

    public static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// Formats a number short: 212.4k, 1.2M, 1,714, 34, 2.1. In the game's language, or in English with invariant
    /// numbers (<paramref name="english"/>) for replies.
    /// </summary>
    public static string Number(double value, bool english = false)
    {
        if (double.IsPositiveInfinity(value))
            return english ? "∞" : Tr.Get("∞");
        if (double.IsNaN(value))
            return "–";
        var abs = Math.Abs(value);
        var c = english ? CultureInfo.InvariantCulture : Tr.Culture;
        return abs switch
        {
            >= 1e9 => Words.Render(Note.Of("{0}B", (value / 1e9).ToString("0.0", c)), english),
            >= 1e6 => Words.Render(Note.Of("{0}M", (value / 1e6).ToString("0.0", c)), english),
            >= 1e5 => Words.Render(Note.Of("{0}k", (value / 1e3).ToString("0.0", c)), english),
            >= 1000 => value.ToString("#,0", c),
            >= 100 => value.ToString("0", c),
            >= 10 => value.ToString("0.#", c),
            >= 1 => value.ToString("0.##", c),
            0 => "0",
            _ => value.ToString("0.###", c),
        };
    }

    /// <summary>
    /// A range of values as results show it: "4,350 – 4,470". In English when <paramref name="english"/>.
    /// </summary>
    public static string Range(double low, double high, bool english = false) =>
        Words.Render(Note.Of("{0} – {1}", Number(low, english), Number(high, english)), english);

    /// <summary>
    /// A share as a percentage: 12%, 0.4%, 99.9%. Only a share of exactly all reads 100%: one survived fight in a
    /// thousand must not look like none. In English with invariant numbers when <paramref name="english"/>.
    /// </summary>
    public static string Percent(double fraction, bool english = false)
    {
        var nearAll = fraction is > 0.9 and < 1;
        var percent = nearAll ? Math.Min(fraction * 100, 99.9) : fraction * 100;
        return Words.Render(
            Note.Of(
                "{0}%",
                percent.ToString(
                    fraction is > 0 and < 0.1 || nearAll ? "0.#" : "0",
                    english ? CultureInfo.InvariantCulture : Tr.Culture
                )
            ),
            english
        );
    }

    /// <summary>A memory size in the game's culture: "500 MB", "1.5 GB".</summary>
    public static string Bytes(long bytes) => Sizes.Bytes(bytes, Tr.Culture, Tr.Get("{0} MB"), Tr.Get("{0} GB"));
}
