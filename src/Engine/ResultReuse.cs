using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>Answers a run's targets from earlier finished results with the same identity and seed.</summary>
internal static class ResultReuse
{
    // The seed a target's reused result must have: its own run seed when the request gives one, else the run's seed
    // when the player set it, else any.
    public static ulong? SeedOf(RunRequest request, string key) =>
        request.TargetSeeds != null && request.TargetSeeds.TryGetValue(key, out var seed) ? seed : request.Seed;

    // Puts earlier finished results with the same identity into the run (seed matched as `seedOf` says), each a
    // copy marked with where it came from, and returns the targets left to simulate.
    public static List<TargetSpec> Reuse(Run run, List<TargetSpec> targets, Func<string, ulong?> seedOf)
    {
        if (RunService.Results is not { } index)
            return [.. targets];
        var simulate = new List<TargetSpec>(targets.Count);
        var opened = new Dictionary<string, Run?>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (
                ResultIdentity.Of(run, target.Key) is not { } identity
                || index.Find(identity, seedOf(target.Key), opened) is not { } found
            )
            {
                simulate.Add(target);
                continue;
            }
            var copy = System.Text.Json.JsonSerializer.Deserialize<TargetResult>(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(found.Result, RunFile.Options),
                RunFile.Options
            )!;
            copy.ReusedFrom = found.Origin;
            copy.Name = run.Results[target.Key].Name;
            run.Results[target.Key] = copy;
        }
        return simulate;
    }
}
