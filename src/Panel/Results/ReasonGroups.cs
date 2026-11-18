using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The headings of the targets whose trial ran over, one per reason, placed after "Died every fight" before the
/// first row of each group, each with its count. A heading with no row is hidden.
/// </summary>
internal sealed class ReasonGroups
{
    private readonly Dictionary<StallReason, (HBoxContainer Row, Label Text)> _headings = [];

    public ReasonGroups(VBoxContainer list)
    {
        foreach (var reason in Enum.GetValues<StallReason>())
        {
            var row = Ui.Add(list, Ui.Row(10, "Cs2ReasonHeading" + reason));
            var text = Ui.Add(row, Ui.Label("", TextStyle.BodyStrong));
            text.LabelSettings = SideLine.Gold(TextStyle.BodyStrong);
            var rule = Ui.Add(row, new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            rule.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.Visible = false;
            _headings[reason] = (row, text);
        }
    }

    /// <summary>Sets each heading's count from <paramref name="table"/>'s rows and hides the empty ones.</summary>
    public void Count(RankTable table)
    {
        var counts = table.ReasonGroups.ToDictionary(g => g.Reason, g => g.Count);
        foreach (var (reason, (row, text)) in _headings)
        {
            var count = counts.GetValueOrDefault(reason);
            row.Visible = count > 0;
            text.Text = Tr.Note(StallReasons.Heading(reason, count));
        }
    }

    /// <summary>The heading to place before <paramref name="row"/>, when it is the first of its group.</summary>
    public Control? Before(RankRow row, StallReason? previous) =>
        row.Stall is { } mark && mark.Reason != previous ? _headings[mark.Reason].Row : null;

    public void Hide()
    {
        foreach (var (row, _) in _headings.Values)
            row.Visible = false;
    }

    /// <summary>The headings shown, in order, for <c>cs2 ui</c>: "group armour 2".</summary>
    public static IEnumerable<string> Describe(RankTable? table) =>
        table?.ReasonGroups.Select(g => $"group {StallReasons.Word(g.Reason).Replace(' ', '-')} {g.Count}") ?? [];

    /// <summary>
    /// A stalled row's reason, whole and wrapped, in the value's place: the player reads it without hovering.
    /// </summary>
    public static void FillReason(Label value, Control bar, RankRow row)
    {
        value.Text = row.Reasons is { } reasons ? Tr.Notes(reasons, " ") : "";
        value.LabelSettings = Look.Style(TextStyle.Body);
        value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.Visible = false;
    }

    /// <summary>Undoes <see cref="FillReason"/> for a row reused for a target with a value.</summary>
    public static void ClearReason(Label value, Control bar)
    {
        value.AutowrapMode = TextServer.AutowrapMode.Off;
        value.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        bar.Visible = true;
    }
}
