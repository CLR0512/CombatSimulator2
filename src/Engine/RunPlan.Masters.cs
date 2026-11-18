using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Engine;

internal static partial class RunPlan
{
    /// <summary>
    /// Each master of the run turned into its monsters: its result holds a queued part per monster of its pool, with
    /// its blocks and what blocking costs, and is refused instead when the build locks all of it. Whether the setup has
    /// the master unlocked is judged on the prepared copy, as any target's requirements are. Returns the monsters to
    /// fight, each fought on its master's task. A master whose monsters are given a loadout the character no longer
    /// has unlocked (<paramref name="usableLoadouts"/>) is refused, so it never runs with another loadout instead.
    /// </summary>
    public static List<TargetSpec> Expand(
        Run run,
        IReadOnlyDictionary<string, MasterSpec?> masters,
        Func<TargetSpec, string> name,
        IReadOnlyCollection<string> usableLoadouts
    )
    {
        var parts = new List<TargetSpec>();
        foreach (var key in run.Targets.Where(Ids.IsMaster))
        {
            var result = run.Results[key];
            if (result.IsFinal)
                continue; // answered from an earlier run, its monsters with it
            if (masters.GetValueOrDefault(key) is not { } master)
            {
                Refuse(result, [Note.Of("no Slayer master {0} in this game", Ids.Brief(Ids.MasterOf(key)))]);
                continue;
            }
            if (GoneLoadouts(run, Ids.MasterOf(key), usableLoadouts) is { Count: > 0 } gone)
            {
                Refuse(result, [.. gone.Select(slot => Note.Of("loadout {0} is gone: change it", Ids.Brief(slot)))]);
                continue;
            }
            result.Blocked = [.. master.Blocked];
            result.Loadouts = MonsterLoadouts(run.Setup, master);
            result.Master = master.Facts;
            result.Parts =
            [
                .. master.Pool.Select(p => new TargetResult
                {
                    Key = p.Key,
                    Kind = p.Kind,
                    Name = name(p),
                    State = TargetState.Queued,
                }),
            ];
            parts.AddRange(master.Pool);
        }
        return parts;
    }

    // Each monster of the master's pool given a loadout of its own by the setup, by its target key.
    private static Dictionary<string, string>? MonsterLoadouts(Setup setup, MasterSpec master)
    {
        var change = setup.Changes?.Find(new ChangeKey(ChangeKind.Loadouts, master.Master));
        if (change is not { Loadouts.Count: > 0 })
            return null;
        var slots = change.Loadouts.ToDictionary(p => p.Enemy, p => p.Slot, StringComparer.Ordinal);
        return master
            .Pool.Where(p => slots.ContainsKey(p.Enemy!))
            .ToDictionary(p => p.TargetKey, p => slots[p.Enemy!], StringComparer.Ordinal);
    }

    // The loadouts a master's monsters were given that the character hasn't got unlocked now. The change as asked is
    // read, not the setup's, which leaves a refused change out.
    private static List<string> GoneLoadouts(Run run, string master, IReadOnlyCollection<string> usable)
    {
        var asked = run.ChangeOutcomes?.Select(o => o.Change) ?? run.Setup.Changes ?? (IEnumerable<Change>)[];
        return
        [
            .. asked
                .Where(c => c.Kind == ChangeKind.Loadouts && c.Subject == master)
                .SelectMany(c => c.Loadouts.Select(p => p.Slot))
                .Where(slot => !usable.Contains(slot))
                .Distinct(),
        ];
    }

    /// <summary>The loadout each of <paramref name="targets"/> given one by the setup wears; null for none.</summary>
    public static IReadOnlyDictionary<string, string>? PartLoadouts(Setup setup, IEnumerable<TargetSpec> targets) =>
        PartSetups.Of(setup, targets.Select(t => t.Key)) is { Count: > 0 } map ? map : null;

    private static void Refuse(TargetResult result, IReadOnlyList<Note> reasons)
    {
        result.State = TargetState.Refused;
        result.Reasons.AddRange(reasons);
    }
}
