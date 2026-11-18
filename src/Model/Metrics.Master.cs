using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Model;

public static partial class Metrics
{
    // A master's metrics are its monsters': it offers whatever they hold, and its figures are worked out from them.
    private static List<TargetResult> Finished(IEnumerable<TargetResult> results) =>
        [
            .. results
                .SelectMany(r =>
                    r.Kind switch
                    {
                        TargetKind.Master => CountedParts(r),
                        TargetKind.Set => Finished(r.Parts ?? []),
                        _ => [r],
                    }
                )
                .Where(r => r.HasEstimates),
        ];

    private static List<TargetResult> CountedParts(TargetResult master) =>
        master.State == TargetState.Finished ? MasterParts.Counted(master) : [];

    private static Estimate? MasterEstimate(TargetResult master, string key, TimeUnit unit, PriceTable? prices) =>
        master.State == TargetState.Finished && MasterParts.Counted(master) is { Count: > 0 } parts
            ? MasterFigures.Estimate(parts, key, unit, prices)
            : null;

    private static (double[] Values, double[] Denominators)? MasterSeries(
        TargetResult master,
        string key,
        TimeUnit unit,
        PriceTable? prices
    ) =>
        master.State == TargetState.Finished && MasterParts.Counted(master) is { Count: > 0 } parts
            ? MasterFigures.Series(parts, key, unit, prices)
            : null;
}
