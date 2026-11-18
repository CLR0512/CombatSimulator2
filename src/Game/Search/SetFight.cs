using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game.Search;

/// <summary>
/// One set fought on targets a search scores together: each target in turn in the same copy, reseeded by its own key,
/// a master as its monsters on its task. The set's weights are the search's to give, once current gear has fought.
/// </summary>
internal static class SetFight
{
    public static TargetResult Fight(
        Copy copy,
        TargetSpec set,
        ChangeList? changes,
        DriveOptions options,
        ulong seed,
        CancellationToken token
    )
    {
        var members = Targets.Members(copy.Game, set);
        var result = new TargetResult
        {
            Key = set.Key,
            Kind = TargetKind.Set,
            State = TargetState.Fighting,
            Parts = [],
        };
        foreach (var member in members)
        {
            if (token.IsCancellationRequested)
                break;
            if (member.Kind == TargetKind.Master)
            {
                result.Parts.Add(MasterFight.Fight(copy, member, changes, options, seed, token));
                continue;
            }
            copy.Reseed(seed, member.Key);
            result.Parts.Add(Driver.Fight(copy, member, Targets.Check(copy.Game, member), options, token));
        }
        if (result.Parts.Count < members.Count || members.Count == 0)
        {
            result.State = TargetState.Cancelled;
            result.Reasons.Add(Note.Of("cancelled"));
            return result;
        }
        result.State = TargetState.Finished;
        result.TrialsDone = result.Parts.Sum(p => p.TrialsDone);
        result.GameSeconds = result.Parts.Sum(p => p.GameSeconds);
        result.NotSimulated = GapNote.Merged(result.Parts);
        return result;
    }
}
