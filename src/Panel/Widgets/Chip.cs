using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>A short piece of text in a box with a ✕ beside it that takes it away: one change in the changes bar.</summary>
internal static class Chip
{
    /// <summary>
    /// A chip named <paramref name="name"/>; its ✕ is named <paramref name="removeName"/> and is left out when
    /// <paramref name="remove"/> is null.
    /// </summary>
    public static PanelContainer Make(string name, string text, string removeName, Action? remove, string tooltip = "")
    {
        var chip = new PanelContainer
        {
            Name = name,
            TooltipText = tooltip,
            MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        PixelBox.Panel(chip, Box(Look.Safe));
        var row = Ui.Add(chip, Ui.Row(6));
        row.AddChild(Ui.Label(text, TextStyle.Label));
        if (remove != null)
        {
            var close = Ui.Button("✕", Look.Button4, removeName, remove);
            close.Flat = true;
            close.TooltipText = Tr.Get("Undo this change");
            row.AddChild(close);
        }
        return chip;
    }

    /// <summary>The chip's edge in <paramref name="colour"/>: a change made by ignoring a requirement is edged amber.</summary>
    public static void SetEdge(PanelContainer chip, Color colour) => PixelBox.Panel(chip, Box(colour));

    // A neutral wash with a coloured edge: the text on it stays the label's own colour.
    private static StyleBoxFlat Box(Color edge)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(1, 1, 1, 0.06f),
            BorderColor = edge,
            ContentMarginLeft = 10,
            ContentMarginRight = 6,
            ContentMarginTop = 3,
            ContentMarginBottom = 3,
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(3);
        return box;
    }
}
