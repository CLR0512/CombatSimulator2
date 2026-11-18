using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>A short statement with an icon before it, on a row panel: one item of a list of facts.</summary>
internal static class IconLine
{
    public static PanelContainer Make(Texture2D? icon, string text, string name)
    {
        var panel = Look.Panel(Art.RowBox, name);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var row = Ui.Add(panel, Ui.Row(10));
        row.AddChild(Ui.Picture(icon, 22));
        row.AddChild(Ui.Label(text, TextStyle.Label, name + "Text", wrap: true));
        return panel;
    }
}
