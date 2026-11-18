using System.Text;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

internal sealed partial class SimulateView
{
    /// <summary>Describes the Simulate view, for <c>cs2 ui</c>.</summary>
    public void Describe(StringBuilder text)
    {
        if (!Ready)
        {
            text.Append("\nsimulate=building");
            return;
        }
        text.Append("\nslot_picker=").Append(_slotPicker.For);
        _strip.Describe(text);
        text.Append("\nrows=").Append(_results.List.Count);
        var (folded, foldable, collapsed) = _results.Chart.Folds;
        text.Append("\nchart bars=")
            .Append(_results.Chart.Bars)
            .Append(" scrolls=")
            .Append(_results.Chart.Scrolls ? "yes" : "no")
            .Append(" toggles=")
            .Append(_results.Chart.Toggles.Toggles)
            .Append(" ignored=")
            .Append(_results.Chart.Toggles.Ignored)
            .Append(Widgets.ChartMarks.Counts(_results.Chart));
        text.Append("\nfolded=").Append(folded).Append('/').Append(foldable).Append(" collapsed=").Append(collapsed);
        text.Append("\ndetails=").Append(_details.Key is { } k ? Ids.BriefKey(k) : "none");
        _details.Describe(text);
        var comparison = RunService.Instance.Comparison;
        var shown = PanelState.ComparisonShown && comparison != null;
        text.Append("\ncompare=")
            .Append(
                PanelState.PickingBaseline ? "picking"
                : PanelState.CompareWith == null ? "off"
                : PanelState.CompareWith == PanelState.SavedRun ? "saved"
                : PanelState.CompareWith == PanelState.WhatIf ? "whatif"
                : "setups"
            )
            .Append(" shown=")
            .Append(shown ? "yes" : "no");
        if (shown)
        {
            text.Append("\ncompare_a=").Append(comparison!.A.Id.Length > 0 ? comparison.A.Id : "gone");
            text.Append("\ncompare_b=").Append(comparison.BStarted ? comparison.B.Id : "waiting");
            text.Append("\ncompare_filter=").Append(PanelState.CompareFilter.ToString().ToLowerInvariant());
            text.Append("\ncompare_order=").Append(PanelState.CompareOrder.ToString().ToLowerInvariant());
            text.Append("\ncompare_warnings=").Append(comparison.Warnings.Count);
            text.Append("\ncompare_rows=").Append(_results.CompareList.Count);
            text.Append("\ncompare_chart_pairs=").Append(_results.CompareChart.Pairs);
            text.Append("\ncompare_note=").Append(_results.CompareList.NoteShown ? "shown" : "hidden");
            text.Append("\ncompare_details=").Append(_compareDetails.Key is { } ck ? Ids.BriefKey(ck) : "none");
            foreach (var row in _results.CompareList.Describe().Take(40))
                text.Append("\ncompare_row ").Append(row);
        }
        text.Append("\npicker=")
            .Append(
                PanelState.PickingBaseline ? "compare"
                : _basePicker.Open ? "base"
                : "closed"
            );
        if (PanelState.PickingBaseline)
            text.Append('\n').Append(_picker.Describe());
        text.Append('\n').Append(_run.Describe());
        _setup.Describe(text);
        if (_check.Error != null)
            text.Append("\nsetup-error=").Append(_check.Error);
        foreach (var row in _results.List.Describe().Take(40))
            text.Append("\nrow ").Append(row);
        foreach (var mark in _results.List.DescribeMarks())
            text.Append('\n').Append(mark);
    }
}
