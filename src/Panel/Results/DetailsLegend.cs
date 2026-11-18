using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The details' legend: the rows' marks as drawn (this target, the middle half, the median) with more on hover, and
/// the small pieces the hover text uses.
/// </summary>
internal static class DetailsLegend
{
    // "◆ this target · ▭ middle half · | median ⓘ" on its own line, the rows' marks as drawn with more on hover, and
    // "vs 32 others" under it. The marks wrap, so a longer language never widens the column past the panel.
    public static (VBoxContainer Block, Label Others) Make(Action<Control, Action<VBoxContainer>> hover)
    {
        var block = Ui.Column(2, "Cs2DetailLegend");
        var row = Ui.Add(block, new HFlowContainer());
        row.AddThemeConstantOverride("h_separation", 6);
        row.AddThemeConstantOverride("v_separation", 2);
        var others = Ui.Label("", TextStyle.Tiny);
        row.AddChild(RangeBox.DiamondIcon());
        row.AddChild(Ui.Label(Tr.Get("this target"), TextStyle.Tiny));
        row.AddChild(Ui.Label("·", TextStyle.Tiny));
        row.AddChild(RangeBox.BoxIcon());
        row.AddChild(Ui.Label(Tr.Get("middle half"), TextStyle.Tiny));
        row.AddChild(Ui.Label("·", TextStyle.Tiny));
        row.AddChild(RangeBox.MedianIcon());
        row.AddChild(Ui.Label(Tr.Get("median"), TextStyle.Tiny));
        // Muted until hovered: a hint, not an alert.
        var info = Ui.Picture(Art.File(Art.InfoPath), 14);
        info.Name = "Cs2DetailInfo";
        info.MouseFilter = Control.MouseFilterEnum.Stop;
        var quiet = new Color(0.75f, 0.75f, 0.75f, 0.45f);
        info.SelfModulate = quiet;
        info.GuiInput += e =>
        {
            if (e is InputEventMouseMotion)
                info.SelfModulate = Colors.White;
        };
        info.MouseExited += () => info.SelfModulate = quiet;
        hover(
            info,
            content =>
            {
                content.AddChild(
                    Ui.Label(
                        Tr.Get("Each row runs from the run's worst value (left) to its best (right)."),
                        TextStyle.Body
                    )
                );
                content.AddChild(Line(RangeBox.DiamondIcon(), Tr.Get("this target")));
                content.AddChild(Line(RangeBox.BoxIcon(), Tr.Get("the middle half of the run's targets")));
                content.AddChild(Line(RangeBox.MedianIcon(), Tr.Get("their median")));
                content.AddChild(
                    Ui.Label(
                        Tr.Get("Where lower is better (kill time, deaths), the lowest is at the right."),
                        TextStyle.Small
                    )
                );
                content.AddChild(
                    Ui.Label(Tr.Get("Hover a row for its best, median and worst values."), TextStyle.Small)
                );
            }
        );
        row.AddChild(info);
        block.AddChild(others);
        return (block, others);
    }

    /// <summary>A short bar in the bars' fill, for legends and hover text.</summary>
    public static Control Swatch()
    {
        var swatch = Bar.Make("", 7);
        swatch.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        swatch.CustomMinimumSize = new Vector2(22, 7);
        Bar.Set(swatch, 1, Look.BarFill);
        return swatch;
    }

    /// <summary>An icon and its text on one line.</summary>
    public static HBoxContainer Line(Control icon, string text, TextStyle style = TextStyle.Body)
    {
        var line = Ui.Row(6);
        line.AddChild(icon);
        line.AddChild(Ui.Label(text, style));
        return line;
    }
}
