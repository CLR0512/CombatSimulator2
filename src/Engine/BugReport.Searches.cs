using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

// The searches a report holds: the kept answers' summaries, and what the last search and Droptimizer ran and found.
internal static partial class BugReport
{
    // Copies, taken on the main thread, where the searches change.
    private static List<SearchRecord> SearchRecords(RunService service) =>
        [
            .. service
                .LastSearches.Values.Where(s => s.Record != null)
                .Select(s => s.Record!.ToJson().Deserialize<SearchRecord>(SearchRecord.Options)!),
        ];

    // The kept answers are read from their file so the report shares nothing with the game's own store; null with
    // neither answers nor searches.
    private static string? Searches(
        string dataRoot,
        IReadOnlyList<SearchRecord> searches,
        HashSet<string> includeSnapshots
    )
    {
        var store = new Model.Search.AnswerStore(dataRoot);
        store.Load();
        if (store.Count == 0 && searches.Count == 0)
            return null;
        foreach (var search in searches)
        {
            if (includeSnapshots.Contains(search.Id))
                search.Snapshot = ReportWriter.SnapshotEntry(search.Id);
        }
        return JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["answers"] = store.Summaries(),
                ["searches"] = searches.Select(s => s.ToJson()).ToList(),
            },
            Json
        );
    }
}
