using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>A banner across the results: icon, title, wrapping body and action buttons.</summary>
internal static class Banner
{
    public static PanelContainer Make(
        string name,
        Color accent,
        Texture2D? icon,
        string title,
        string body,
        params Button[] actions
    )
    {
        var panel = new PanelContainer { Name = name };
        PixelBox.Panel(
            panel,
            new StyleBoxFlat
            {
                BgColor = accent.Darkened(0.72f),
                BorderColor = accent,
                BorderWidthLeft = 4,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                ContentMarginLeft = 14,
                ContentMarginRight = 10,
                ContentMarginTop = 8,
                ContentMarginBottom = 8,
            }
        );
        var row = Ui.Add(panel, Ui.Row(12));
        if (icon != null)
            row.AddChild(Ui.Picture(icon, 28));
        var text = Ui.Add(row, Ui.Column(2));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(Ui.Label(title, TextStyle.BodyStrong));
        if (body.Length > 0)
            text.AddChild(Ui.Label(body, TextStyle.Small, wrap: true));
        foreach (var action in actions)
        {
            action.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(action);
        }
        return panel;
    }
}
