using Godot;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The mark on a target where every fight died: a red-edged badge that says so, where a deaths badge or a place
/// would otherwise be. Its text is light on the dark red fill, never red on red.
/// </summary>
internal static class DiedMark
{
    /// <summary>What the mark says, also where it is only text.</summary>
    public static string Text => Tr.Get("Died every fight");

    /// <summary>Why such a target has no rank, for hover text.</summary>
    public static string Why =>
        Tr.Get("You died in every fight here. Its values were earned while dying: no rank, never best.");

    public static PanelContainer Make(string name)
    {
        var box = new PanelContainer
        {
            Name = name,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = Why,
        };
        var style = new StyleBoxFlat
        {
            BgColor = Look.DeadlyFill,
            BorderColor = Look.Deadly,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
        };
        style.SetBorderWidthAll(1);
        PixelBox.Panel(box, style);
        var label = Ui.Label(Text, TextStyle.Label);
        label.LabelSettings = Compare.CompareText.Coloured(Look.Deadly.Lerp(Colors.White, 0.75f));
        box.AddChild(label);
        return box;
    }
}
