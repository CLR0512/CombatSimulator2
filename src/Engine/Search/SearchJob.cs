using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// One stage of a search: the base (the setup's snapshot, loadout and changes), one target, and the candidates to
/// fight for the options' trials, all from one seed. Each candidate's changes go on top of the base's. On a master,
/// a candidate fights only the monsters on the setup; with <paramref name="OwnLoadouts"/>, only those with a loadout
/// of their own, which every candidate shares. With <paramref name="Checks"/>, a race fought once: each candidate
/// fights once to the options' trials, is read at the checks, and stops where the rule drops it.
/// </summary>
internal sealed record SearchJob(
    Snapshot Snapshot,
    string? LoadoutSlot,
    ChangeList? BaseChanges,
    TargetSpec Target,
    DriveOptions Options,
    ulong Seed,
    int Copies,
    IReadOnlyList<Candidate> Candidates,
    bool OwnLoadouts = false,
    CheckRule? Checks = null
)
{
    /// <summary>
    /// What a candidate fights with: the base's changes with the candidate's over them, as the setup of a what-if
    /// run of that set would list them.
    /// </summary>
    public ChangeList ChangesFor(Candidate candidate)
    {
        if (BaseChanges is not { IsEmpty: false } changes)
            return candidate.Changes;
        foreach (var change in candidate.Changes)
            changes = changes.With(change);
        return changes;
    }
}
