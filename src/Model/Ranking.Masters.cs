using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Model;

public static partial class Ranking
{
    // A finished master without figures gets stuck on a monster: its row says which and why, and it ranks with the
    // rows that have no estimate, after every master with figures.
    private static Note? Stuck(TargetResult? result) =>
        result is { Kind: TargetKind.Master, State: TargetState.Finished }
        && MasterParts.Counted(result) is var parts
        && MasterFigures.Stuck(parts) is { } stuck
            ? Note.Of("Gets stuck on {0}: {1}", NameOf(stuck.Key, parts), stuck.Reason)
            : null;

    private static string NameOf(string key, List<TargetResult> parts) =>
        parts.Find(p => p.Key == key)?.Name is { Length: > 0 } name ? name : Ids.BriefKey(Ids.PartTarget(key));
}
