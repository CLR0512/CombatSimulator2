using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Game.Slayer;

/// <summary>Which of a master's monsters a search's fight takes.</summary>
internal enum PartsFought
{
    /// <summary>Every monster, each with a loadout of its own wearing it.</summary>
    All,

    /// <summary>The monsters on the setup; the others are left queued for the search to fill in.</summary>
    Shared,

    /// <summary>Only the monsters with a loadout of their own; the others are left queued.</summary>
    OwnLoadouts,
}

/// <summary>
/// One set fought on a master in a search: each monster of the master's pool, as the copy's setup has it (its blocks
/// included), fought on the master's task one after another in the same copy, each reseeded by its own key, then
/// assembled into the master's result as a run assembles it. A monster given a loadout of its own wears it, after the
/// others, and the copy goes back to its own loadout after; a search fights those once for every set it tries
/// (<see cref="PartsFought"/>).
/// </summary>
internal static class MasterFight
{
    public static TargetResult Fight(
        Copy copy,
        TargetSpec master,
        ChangeList? changes,
        DriveOptions options,
        ulong seed,
        CancellationToken token,
        PartsFought which = PartsFought.All
    )
    {
        var result = new TargetResult
        {
            Key = master.Key,
            Kind = TargetKind.Master,
            State = TargetState.Queued,
        };
        if (SlayerTasks.Read(copy.Game, master.Master!, changes) is not { } spec)
        {
            result.State = TargetState.Refused;
            result.Reasons.Add(Note.Of("no Slayer master {0} in this game", Ids.Brief(master.Master!)));
            return result;
        }
        result.Blocked = [.. spec.Blocked];
        result.Master = spec.Facts;
        var own = PartSetups.Of(changes, spec.Pool.Select(p => p.Key));
        var parts = spec
            .Pool.Select(p => new TargetResult
            {
                Key = p.Key,
                Kind = p.Kind,
                State = TargetState.Queued,
            })
            .ToArray();
        var fought = 0;
        var asked = 0;
        void FightPart(int i)
        {
            asked++;
            if (token.IsCancellationRequested)
                return;
            var part = spec.Pool[i];
            copy.Reseed(seed, part.Key);
            parts[i] = Driver.Fight(copy, part, Targets.Check(copy.Game, part), options, token);
            fought++;
        }
        if (which != PartsFought.OwnLoadouts)
        {
            for (var i = 0; i < parts.Length; i++)
            {
                if (!own.ContainsKey(spec.Pool[i].Key))
                    FightPart(i);
            }
        }
        if (which != PartsFought.Shared && own.Count > 0)
        {
            var wore = copy.CurrentLoadout;
            foreach (
                var group in Enumerable
                    .Range(0, parts.Length)
                    .Where(i => own.ContainsKey(spec.Pool[i].Key))
                    .GroupBy(i => own[spec.Pool[i].Key], StringComparer.Ordinal)
            )
            {
                copy.WearLoadout(group.Key);
                foreach (var i in group)
                    FightPart(i);
            }
            copy.WearLoadout(wore);
        }
        result.Parts = [.. parts];
        result.Loadouts =
            own.Count > 0 ? own.ToDictionary(p => Ids.PartTarget(p.Key), p => p.Value, StringComparer.Ordinal) : null;
        if (fought < asked || parts.Length == 0)
        {
            MasterParts.End(result, TargetState.Cancelled, Note.Of("cancelled"));
            return result;
        }
        MasterParts.Settle(result);
        return result;
    }
}
