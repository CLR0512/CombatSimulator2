using Godot;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>One step of a sequence: an icon beside a short title and one line saying what happens in it.</summary>
internal static class TimelineStep
{
    public static HBoxContainer Make(Texture2D? icon, string title, string text, string name)
    {
        var row = Ui.Row(14, name);
        row.AddChild(Ui.Picture(icon, 34));
        var words = Ui.Add(row, Ui.Column(2));
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        words.AddChild(Ui.Label(title, TextStyle.BodyStrong));
        words.AddChild(Ui.Label(text, TextStyle.Small, name + "Text", wrap: true));
        return row;
    }
}
