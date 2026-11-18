using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>
/// What the saved-runs rows were built from: the store's contents, its keep limit, the runs the comparison holds
/// and whether the list is open. While a new stamp equals the one the rows were built at, the rows on screen still
/// say the same and needn't be made again.
/// </summary>
internal readonly record struct HistoryStamp(long StoreChanges, int Max, string ComparisonRuns, bool Expanded)
{
    public static HistoryStamp Of(RunStore store, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new(store.Changes, store.Max, string.Join(',', store.Pinned().Order(StringComparer.Ordinal)), expanded);
    }
}
