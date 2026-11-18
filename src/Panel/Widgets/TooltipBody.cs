using System.Text;
using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A tooltip's text in the hover panel. Plain text is one label. Text with <see cref="Marks"/> is one rich-text
/// control that draws each marked part in its colour. A long line wraps, and a text too tall for the window scrolls.
/// </summary>
internal static class TooltipBody
{
    private const float MaxWidth = 440;
    private static readonly Color MutedColour = Color.FromHtml("#8f8a82");

    /// <summary>
    /// Puts <paramref name="text"/> in <paramref name="content"/>. A text taller than <paramref name="maxHeight"/>
    /// goes in a scroll of that height, which is returned for the wheel to turn; null when it all fits.
    /// </summary>
    public static ScrollContainer? Fill(VBoxContainer content, string text, float maxHeight)
    {
        Control body = Marks.In(text) ? Rich(content, text) : Plain(content, text);
        var size = body.CustomMinimumSize;
        if (size.Y <= maxHeight)
            return null;
        // The pointer never reaches a tooltip, so its scroll takes no mouse: the wheel turns it from where it is.
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        scroll.GetVScrollBar().MouseFilter = Control.MouseFilterEnum.Ignore;
        scroll.CustomMinimumSize = new Vector2(
            size.X + scroll.GetVScrollBar().GetCombinedMinimumSize().X + 6,
            maxHeight
        );
        content.RemoveChild(body);
        scroll.AddChild(body);
        content.AddChild(scroll);
        return scroll;
    }

    // Sized here, in the frame the panel is placed, before layout would give the text its size.
    private static Label Plain(VBoxContainer content, string text)
    {
        var label = Ui.Label(text, TextStyle.Small);
        content.AddChild(label);
        var size = label.GetMinimumSize();
        if (size.X > MaxWidth)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.Size = new Vector2(MaxWidth, 0);
            var lines = label.GetLineCount();
            var spacing = label.LabelSettings?.LineSpacing ?? label.GetThemeConstant("line_spacing");
            size = new Vector2(MaxWidth, lines * label.GetLineHeight() + (lines - 1) * spacing);
        }
        label.CustomMinimumSize = size;
        return label;
    }

    private static RichTextLabel Rich(VBoxContainer content, string text)
    {
        var style = Look.Style(TextStyle.Small);
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        if (style.Font != null)
            rich.AddThemeFontOverride("normal_font", style.Font);
        rich.AddThemeFontSizeOverride("normal_font_size", style.FontSize);
        rich.AddThemeColorOverride("default_color", style.FontColor);
        rich.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        rich.Text = Bbcode(text);
        content.AddChild(rich);
        var width = rich.GetContentWidth();
        if (width > MaxWidth)
        {
            rich.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            rich.Size = new Vector2(MaxWidth, 0);
            width = (int)MaxWidth;
        }
        rich.CustomMinimumSize = new Vector2(width, rich.GetContentHeight());
        return rich;
    }

    // The marks as colour tags; a "[" of the text itself is escaped, as rich text reads it as a tag.
    private static string Bbcode(string text)
    {
        var bb = new StringBuilder(text.Length + 64);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '[')
                bb.Append("[lb]");
            else if (c == Marks.Side && i + 1 < text.Length)
                bb.Append("[color=#").Append(Look.Best.ToHtml(false)).Append(']').Append(text[++i]).Append("[/color]");
            else if (c == Marks.Open && i + 1 < text.Length)
                bb.Append("[color=#").Append(Colour(text[++i]).ToHtml(false)).Append(']');
            else if (c == Marks.Close)
                bb.Append("[/color]");
            else
                bb.Append(c);
        }
        return bb.ToString();
    }

    private static Color Colour(char code) =>
        code switch
        {
            Marks.GoodCode => Look.Safe,
            Marks.BadCode => Look.Deadly,
            _ => MutedColour,
        };
}
