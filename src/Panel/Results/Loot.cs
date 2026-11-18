using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>What a result gained: items and currencies as rates in a time unit.</summary>
internal static class Loot
{
    /// <summary>Items and currencies gained, most first, as a rate in the unit.</summary>
    public static List<(string Key, double Value)> All(TargetResult result, TimeUnit unit, PriceTable prices)
    {
        if (!result.HasFigures)
            return [];
        // A master's drops are its monsters'.
        var batches =
            result.Kind == TargetKind.Master
                ? Model.Slayer.MasterParts.Counted(result).SelectMany(p => p.Batches)
                : result.Batches;
        var keys = batches.SelectMany(b =>
            b.Gained.Keys.Select(k => "item:" + k).Concat(b.Currency.Keys.Select(k => "currency:" + k))
        );
        return
        [
            .. keys.Distinct(StringComparer.Ordinal)
                .Select(k => (k, Metrics.Estimate(result, k, unit, prices)?.Point ?? 0))
                .Where(p => p.Item2 > 0)
                .OrderByDescending(p => p.Item2),
        ];
    }

    /// <summary>The <paramref name="count"/> largest of <see cref="All"/>, at the panel's prices.</summary>
    public static List<(string Key, double Value)> Top(TargetResult? result, TimeUnit unit, int count) =>
        result == null ? [] : [.. All(result, unit, PanelState.Prices()).Take(count)];

    /// <summary>"Loot per hour": the loot's title in the unit.</summary>
    public static string Title(TimeUnit unit) =>
        unit switch
        {
            TimeUnit.Kill => Tr.Get("Loot per kill"),
            TimeUnit.Second => Tr.Get("Loot per second"),
            TimeUnit.Minute => Tr.Get("Loot per minute"),
            TimeUnit.Hour => Tr.Get("Loot per hour"),
            _ => Tr.Get("Loot per day"),
        };
}
