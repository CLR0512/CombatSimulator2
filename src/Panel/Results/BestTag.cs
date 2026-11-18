using Godot;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The "Best" tag: light text on a dark tag with a gold edge and square corners like the panel's other tags, so the best target is named in
/// words and not by its colour alone.
/// </summary>
internal static class BestTag
{
    public static PanelContainer Make(string name, bool small = false)
    {
        var box = new PanelContainer
        {
            Name = name,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat
        {
            BgColor = Colors.Black with { A = 0.35f },
            BorderColor = Look.Best,
            ContentMarginLeft = small ? 5 : 7,
            ContentMarginRight = small ? 5 : 7,
        };
        style.SetBorderWidthAll(1);
        PixelBox.Panel(box, style);
        box.AddChild(Ui.Label(small ? Tr.Get("best") : Tr.Get("Best"), small ? TextStyle.Tiny : TextStyle.Label));
        return box;
    }
}
