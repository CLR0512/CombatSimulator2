using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The "ran over" chip under a ranked row's deaths badge: the share of trials that passed the limit, in a pink of
/// its own with light text, the band set by the deaths badge's threshold. Hidden with none.
/// </summary>
internal static class RanOverChip
{
    public static PanelContainer Make(string name)
    {
        var chip = new PanelContainer
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            Visible = false,
        };
        chip.AddChild(Ui.Label("", TextStyle.Label));
        return chip;
    }

    /// <summary>The width of the widest chip, "99.9% ran over", so a column that may hold one keeps one width.</summary>
    public static float Widest()
    {
        var style = Look.Style(TextStyle.Label);
        var text = Tr.Format("{0} ran over", Ui.Percent(0.999));
        return Mathf.Ceil(style.Font.GetStringSize(text, fontSize: style.FontSize).X) + 2 * Sides;
    }

    // The chip's room either side of its text, in a row.
    private const int Sides = 8;

    /// <summary>
    /// Shows <paramref name="share"/> ("1.2% ran over"), with the label's side letter when given ("B: 3.1% ran
    /// over"); hides the chip when null. The hover gives how many trials ran over, and why.
    /// </summary>
    public static void Set(PanelContainer chip, double? share, TargetResult? result, string? side = null)
    {
        if (share is not { } value)
        {
            chip.Visible = false;
            return;
        }
        var label = (Label)chip.GetChild(0);
        // Translators: Chip on a results row: the share of trials that ran over the time limit, e.g. "1.2% ran over".
        var text = Tr.Format("{0} ran over", Ui.Percent(value));
        label.Text = side == null ? text : side + ": " + text;
        PixelBox.Panel(chip, Style(value, Sides, 2));
        chip.TooltipText = result == null ? "" : Hover(result);
        chip.Visible = true;
    }

    /// <summary>
    /// The hover's text in a box of the chip's colours, as the chart's hover and the details show it; wraps to a
    /// readable width.
    /// </summary>
    public static PanelContainer Box(TargetResult result)
    {
        var share = (double)result.RanOver / Math.Max(1, result.Batches.Sum(b => b.Trials));
        var box = new PanelContainer { Name = "Cs2RanOverBox" };
        PixelBox.Panel(box, Style(share, 10, 6));
        var label = Ui.Label(Hover(result), TextStyle.Small, wrap: true);
        label.CustomMinimumSize = new Vector2(320, 0);
        box.AddChild(label);
        return box;
    }

    /// <summary>The chip's box in a band's colours, with the given margins.</summary>
    public static StyleBoxFlat Style(double share, int sides, int ends)
    {
        var style = new StyleBoxFlat
        {
            BgColor = Look.RanOverBack(share),
            BorderColor = Look.RanOverColour(share),
            ContentMarginLeft = sides,
            ContentMarginRight = sides,
            ContentMarginTop = ends,
            ContentMarginBottom = ends,
        };
        style.SetBorderWidthAll(1);
        return style;
    }

    /// <summary>"12 of 1,000 trials ran over 5 minutes. Too slow: …"; empty without a stall record.</summary>
    public static string Hover(TargetResult result)
    {
        if (result.Stall is not { } stall)
            return "";
        var trials = result.Batches.Sum(b => b.Trials);
        return Tr.Note(
            Note.Of(
                "{0} of {1} trials ran over {2} minutes. {3}",
                Ui.Number(result.RanOver),
                Ui.Number(trials),
                Math.Round(stall.LimitSeconds / 60, 1),
                StallReasons.Note(stall)
            )
        );
    }
}
