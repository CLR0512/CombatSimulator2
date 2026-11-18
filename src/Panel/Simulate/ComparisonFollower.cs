using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// Follows comparisons started anywhere (here or <c>cs2 compare</c>): a new one shows as one, with the setup bar
/// set to what it compares; the saved one shows at start when it is the latest.
/// </summary>
internal sealed class ComparisonFollower
{
    private ComparedRuns? _seenComparison;
    private bool _comparisonLoaded;

    /// <summary>Marks a comparison this view started as shown already, so it isn't followed again.</summary>
    public void Seen(ComparedRuns comparison) => _seenComparison = comparison;

    /// <summary>
    /// Shows the service's latest comparison if it is new, with <paramref name="run"/> the current run.
    /// </summary>
    public void Follow(RunService service, Run? run)
    {
        // A swap of the one shown exchanged the sides with it already: following it would set them again.
        for (var from = service.Comparison?.SwappedFrom; from != null; from = from.SwappedFrom)
            if (ReferenceEquals(from, _seenComparison))
                _seenComparison = service.Comparison;
        if (service.Comparison is { } latest && !ReferenceEquals(latest, _seenComparison))
        {
            var fromDisk = !_comparisonLoaded && latest.IsFinal;
            _seenComparison = latest;
            // The comparison saved from the last session shows again only beside the sides the player left: the
            // sides are saved with the character, and a comparison read back never brings a side B back or
            // changes one.
            if (fromDisk)
            {
                if (PanelState.B != null && (ReferenceEquals(run, latest.B) || run?.Id == latest.B.Id))
                    PanelState.ShowComparison(true);
            }
            else if (latest.Reopened)
            {
                // A kept comparison opened again shows as it finished; the sides being set up are the player's.
                PanelState.ShowComparison(true);
            }
            else
            {
                // The setup bar shows what this comparison compares, whatever it showed before: side A's base first,
                // since a side B on the same base starts from it.
                var saved = latest.Record.Kind == ComparisonKind.NowAgainstSaved;
                if (!saved && PanelState.A.Base.Text != latest.A.Setup.Loadout)
                    PanelState.SetSide(SideLetter.A, PanelState.A.WithBase(SideBase.Parse(latest.A.Setup.Loadout)));
                var sameBase = latest.A.Setup.Loadout == latest.B.Setup.Loadout;
                var with =
                    saved ? PanelState.SavedRun
                    : sameBase ? PanelState.WhatIf
                    : latest.B.Setup.Loadout;
                var baseline = saved ? latest.A.Id : null;
                if (PanelState.CompareWith != with || PanelState.Baseline != baseline)
                    PanelState.SetCompareWith(with, baseline);
                // Side B shows the changes it was compared with.
                if (!saved && PanelState.B is { CanChange: true } b)
                    PanelState.SetSide(SideLetter.B, b.WithChanges(latest.B.Setup.Changes ?? ChangeList.Empty));
                PanelState.ShowComparison(true);
            }
        }
        _comparisonLoaded = true;
    }
}
