using System.Reflection;
using GamesByMalcs.Melvor2.Modifiers;
using MelvorMods.CombatSimulator2.Common;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The game keeps each modifier total as a running sum, adding an entry's value when it comes and taking it away
/// when it goes, so taking off what was put on can leave a total a hair away from zero: 0.02 + 0.03 − 0.02 − 0.03
/// is 3.5e-18. A chance that is above zero is rolled, so a copy left so draws once more per roll and every fight
/// after it differs. A copy that is put back sets each total with nothing left in it to zero again.
/// </summary>
internal static class ModifierTotals
{
    // A table hands each new entry to the queries that subscribed to it: they are the queries the game keeps.
    private static readonly FieldInfo? EntryAdded = typeof(ModifierTable).GetField(
        "EntryAdded",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    /// <summary>Sets every total that has no entries left to zero; returns how many were not.</summary>
    public static int Settle(MelvorGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var settled = 0;
        foreach (var query in Queries(game).Where(q => q.Entries.Count == 0 && q.Total != 0))
        {
            query.AdjustTotal(query.Total, 0.0);
            settled++;
        }
        return settled;
    }

    private static bool _warned;

    private static List<ModifierQuery> Queries(MelvorGame game)
    {
        if (EntryAdded == null)
        {
            if (!_warned)
                Log.Warn("the game's modifier tables have no EntryAdded field: totals aren't settled");
            _warned = true;
            return [];
        }
        var system = game.Modifiers;
        var seen = new HashSet<ModifierQuery>(ReferenceEqualityComparer.Instance);
        var queries = new List<ModifierQuery>();
        foreach (var table in system.ModifiableStatTables.Values.Concat(system.StatGroupTables.Values))
        {
            if (EntryAdded.GetValue(table) is not Delegate handlers)
                continue;
            foreach (var handler in handlers.GetInvocationList())
                if (handler.Target is ModifierQuery query && seen.Add(query))
                    queries.Add(query);
        }
        return queries;
    }
}
