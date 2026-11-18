using Godot;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>What the About window's sections share: their headings, tinted text and buttons with content.</summary>
internal static class AboutParts
{
    private static readonly Color HeadingColour = Color.FromHtml("#e8d98c");

    public static Label Heading(string text) => Tinted(text, TextStyle.Heading, HeadingColour);

    // A label in a style's font and size with another colour. Set on the label, not as label settings of its own,
    // which would hold CS2's fonts past the window.
    public static Label Tinted(string text, TextStyle style, Color colour, string name = "")
    {
        var label = Ui.Label(text, style, name);
        var basis = Look.Style(style);
        label.LabelSettings = null;
        label.AddThemeFontOverride("font", basis.Font);
        label.AddThemeFontSizeOverride("font_size", basis.FontSize);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }

    // Lays content over a button, which keeps the press, the hover and the focus: nothing in the content takes the
    // mouse.
    public static void Cover(Button button, Control content)
    {
        button.AddChild(content);
        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Quiet(content);
    }

    private static void Quiet(Node node)
    {
        if (node is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
            control.TooltipText = "";
        }
        foreach (var child in node.GetChildren())
            Quiet(child);
    }
}
