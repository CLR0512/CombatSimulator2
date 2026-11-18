using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// One row of a help window: an icon, a short title and one line, on a row panel. With detail, the row starts folded
/// and a click on it opens the detail under the title, so the window reads at a glance and the detail is one click away.
/// </summary>
internal static class FoldStep
{
    private const int IconSize = 26;
    private const int Gap = 10;

    public static PanelContainer Make(string iconPath, string title, string line, string name, string detail = "")
    {
        var panel = Look.Panel(Art.RowBox, name);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var column = Ui.Add(panel, Ui.Column(6));
        var head = Ui.Add(column, Ui.Row(Gap));
        head.AddChild(Ui.Picture(Art.File(iconPath) ?? Art.Missing, IconSize));
        var titleLabel = Ui.Add(head, Ui.Label(title, TextStyle.BodyStrong));
        titleLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var lineLabel = Ui.Add(head, Ui.Label(line, TextStyle.Label, name + "Line", wrap: true));
        lineLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lineLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (detail.Length == 0)
            return panel;

        var chevron = Ui.Add(head, Ui.Label("▸", TextStyle.Label, name + "Chevron"));
        chevron.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // Under the title, past the icon, so the detail reads as the row's own.
        var detailLabel = Ui.Add(
            column,
            Ui.Margin(Ui.Label(detail, TextStyle.Small, name + "Detail", wrap: true), IconSize + Gap, 0, 0, 0)
        );
        detailLabel.Visible = false;
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        panel.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        panel.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                return;
            detailLabel.Visible = !detailLabel.Visible;
            chevron.Text = detailLabel.Visible ? "▾" : "▸";
        };
        return panel;
    }
}
