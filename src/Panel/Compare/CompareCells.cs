using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Compare;

/// <summary>
/// The cells of the comparison's table: a name, A and B right-aligned under gold heads, and B against A,
/// so every row's numbers line up down the panel.
/// </summary>
internal static class CompareCells
{
    private const float ValueWidth = 72;
    private const float ChangeWidth = 60;

    /// <summary>A section's column heads: its title, then A and B in gold over their values, and "B vs A".</summary>
    public static HBoxContainer Heads(string title)
    {
        var heads = Ui.Row(8);
        var name = Ui.Add(heads, Ui.Label(title, TextStyle.Label));
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        foreach (var letter in new[] { "A", "B" })
        {
            var head = Ui.Add(heads, Ui.Label(letter, TextStyle.Label));
            head.LabelSettings = SideLine.Gold(TextStyle.Label);
            head.CustomMinimumSize = new Vector2(ValueWidth, 0);
            head.HorizontalAlignment = HorizontalAlignment.Right;
        }
        // Translators: Column head over the difference of B from A in the comparison's table.
        var against = Ui.Add(heads, Ui.Label(Tr.Get("B vs A"), TextStyle.Tiny));
        against.CustomMinimumSize = new Vector2(ChangeWidth, 0);
        against.HorizontalAlignment = HorizontalAlignment.Right;
        return heads;
    }

    /// <summary>A side's value, right-aligned in its column.</summary>
    public static Label Value(string text = "")
    {
        var value = Ui.Label(text, TextStyle.BodyStrong);
        value.CustomMinimumSize = new Vector2(ValueWidth, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        return value;
    }

    /// <summary>The "B vs A" cell, filled by <see cref="SetDifference"/>.</summary>
    public static Label Change()
    {
        var change = Ui.Label("", TextStyle.Small);
        change.CustomMinimumSize = new Vector2(ChangeWidth, 0);
        change.HorizontalAlignment = HorizontalAlignment.Right;
        return change;
    }

    // Room between a row's text and the lines above and below it.
    private const int RowPad = 6;

    /// <summary>
    /// A row of the table: <c>Content</c> takes the row's lines, kept off the lines around it, and the line under it
    /// closes <c>Root</c>.
    /// </summary>
    public static (VBoxContainer Root, VBoxContainer Content) Row(string name = "")
    {
        var root = Ui.Column(0, name);
        var pad = Ui.Add(root, new MarginContainer());
        pad.AddThemeConstantOverride("margin_top", RowPad);
        pad.AddThemeConstantOverride("margin_bottom", RowPad);
        var content = Ui.Add(pad, Ui.Column(2));
        root.AddChild(new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        return (root, content);
    }

    /// <summary>
    /// B against A in the verdict's colour: "+262%", "different skills" or "–". A change too small to tell from luck
    /// still shows its size, in the neutral colour, with its range and why in the tooltip.
    /// </summary>
    public static void SetDifference(Label label, Difference d)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(d);
        label.TooltipText = "";
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (d.SkillsDiffer)
        {
            // Translators: Short for "different skills" in a narrow column: A and B train different skills.
            label.Text = Tr.Get("diff. skills");
            label.TooltipText = Tr.Get("A and B train different skills, so their XP isn't compared");
            label.MouseFilter = Control.MouseFilterEnum.Pass;
            label.LabelSettings = Look.Style(TextStyle.Tiny);
        }
        else if (d.OnlyKills is { } only)
        {
            // Translators: Short, in a narrow comparison column: only side {0} (A or B) can kill the target in time.
            label.Text = Tr.Format("only {0}", only);
            label.LabelSettings = CompareText.Coloured(DiffBar.Colour(d.Verdict, label), TextStyle.Small);
            if ((only == "B" ? d.StallA : d.StallB) is { } stall)
            {
                // Translators: Side {0} (A or B) can't kill the target in time; {1} says why, e.g. "Armour stops your hits".
                label.TooltipText = Tr.Format(
                    "{0} can't kill it in time: {1}",
                    [only == "B" ? "A" : "B", Tr.Note(StallReasons.Brief(stall.Reason))]
                );
                label.MouseFilter = Control.MouseFilterEnum.Stop;
            }
        }
        else if (d.Delta != null)
        {
            label.Text =
                d.Percent != null ? CompareText.SignedPercent(d.Percent.Point) : CompareText.Signed(d.Delta.Point);
            label.LabelSettings = CompareText.Coloured(DiffBar.Colour(d.Verdict, label), TextStyle.Small);
            if (d.Verdict == Verdict.NoClearDifference)
            {
                label.MouseFilter = Control.MouseFilterEnum.Stop;
                var range = d.Percent is { } p
                    ? (CompareText.SignedPercent(p.Low), CompareText.SignedPercent(p.High))
                    : (CompareText.Signed(d.Delta.Low), CompareText.Signed(d.Delta.High));
                // Translators: Tooltip on a change too small to tell from luck: {0} and {1} the ends of its 95%
                // range, "-15%" and "+2%".
                label.TooltipText = Tr.Format(
                    "Could be luck: likely between {0} and {1}. More trials narrow it.",
                    [range.Item1, range.Item2]
                );
            }
        }
        else
        {
            label.Text = "–";
            label.LabelSettings = Look.Style(TextStyle.Small);
        }
    }

    /// <summary>A side's value; for a fight number, why a side has none: no counts kept, or its trial ran over.</summary>
    public static string Side(TargetResult? result, Estimate? estimate, string key)
    {
        if (estimate != null)
            return FightNumbers.IsShare(key) ? Ui.Percent(estimate.Point) : Ui.Number(estimate.Point);
        if (!FightNumbers.IsFightKey(key) || result == null)
            return "–";
        return result is { HasEstimates: false, Stall: not null } ? Tr.Get("Unfinished")
            : !result.HasFightCounts ? Tr.Get("Not recorded")
            : "–";
    }
}
