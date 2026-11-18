using System.Globalization;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>A run's timings on one line, as <c>cs2 status</c> shows them, with the two figures worked out from them.</summary>
internal static class RunTimingsText
{
    /// <summary>
    /// The line: the stored figures, then clock time per target fought and the overhead over fight time shared by
    /// the slots. The last two are left out when nothing fought or the run was saved before slots were counted.
    /// </summary>
    public static string Line(RunTimings t)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"timings first {t.FirstResult:0.00} total {t.Total:0.00} snapshot {t.Snapshot * 1000:0.0} wait {t.HostWait:0.00} check {t.Check:0.00} builds {t.Builds} ({t.FromStock} stock) {t.BuildsSummed:0.00} prepare {t.PrepareSummed:0.00} resets {t.Resets} ({t.ResetMisses} missed) {t.ResetSummed:0.00} fight {t.FightSummed:0.00} report {t.Report * 1000:0} reused {t.Reused} fought {t.Fought} idle {t.IdleSummed:0.00} tail {t.Tail:0.00}"
        );
        if (PerTargetMs(t) is not { } perTarget || Overhead(t) is not { } overhead)
            return line;
        return line
            + string.Create(CultureInfo.InvariantCulture, $" per-target {perTarget:0} overhead {overhead * 100:0}%");
    }

    /// <summary>Clock milliseconds per target fought; null when nothing fought.</summary>
    public static double? PerTargetMs(RunTimings t) => t.Fought > 0 ? t.Total * 1000 / t.Fought : null;

    /// <summary>
    /// How much longer the run took than its fight time shared evenly by its slots, as a fraction (0.3 = 30%);
    /// null when nothing fought or the slots weren't counted.
    /// </summary>
    public static double? Overhead(RunTimings t) =>
        t is { Fought: > 0, Slots: > 0, FightSummed: > 0 } ? t.Total / (t.FightSummed / t.Slots) - 1 : null;
}
