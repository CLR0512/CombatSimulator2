namespace MelvorMods.CombatSimulator2.Model.Slayer;

/// <summary>
/// A master's monsters inside a run: each is fought and reported as a target of its own, by its own key, and kept in
/// its master's result, whose state follows theirs.
/// </summary>
public static class MasterParts
{
    /// <summary>
    /// The monsters a master's figures are over: its parts but the blocked ones, which are fought so a block suggestion
    /// can weigh unblocking them.
    /// </summary>
    public static List<TargetResult> Counted(TargetResult master)
    {
        ArgumentNullException.ThrowIfNull(master);
        var blocked = master.Blocked?.ToHashSet(StringComparer.Ordinal) ?? [];
        return [.. (master.Parts ?? []).Where(p => !blocked.Contains(Ids.PartTarget(p.Key)))];
    }

    /// <summary>The result for <paramref name="key"/>: a target's own, or a master's monster inside its master.</summary>
    public static TargetResult? Find(Run run, string key)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(key);
        if (!Ids.IsPart(key))
            return run.Results.GetValueOrDefault(key);
        return run.Results.GetValueOrDefault(Ids.MasterKey(Ids.MasterOf(key)))?.Parts?.Find(p => p.Key == key);
    }

    /// <summary>
    /// Puts a result in place of the one with its key, as <see cref="Find"/> finds it, and settles its master; false
    /// when the run has no such target.
    /// </summary>
    public static bool Put(Run run, TargetResult result)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(result);
        if (!Ids.IsPart(result.Key))
        {
            if (!run.Results.ContainsKey(result.Key))
                return false;
            run.Results[result.Key] = result;
            return true;
        }
        if (run.Results.GetValueOrDefault(Ids.MasterKey(Ids.MasterOf(result.Key))) is not { Parts: { } parts } master)
            return false;
        var index = parts.FindIndex(p => p.Key == result.Key);
        if (index < 0)
            return false;
        parts[index] = result;
        Settle(master);
        return true;
    }

    /// <summary>
    /// A master's state from its monsters': fighting while any is under way, final once all are, cancelled when any
    /// was; its totals are theirs summed, and its assumptions the master's own.
    /// </summary>
    public static void Settle(TargetResult master)
    {
        ArgumentNullException.ThrowIfNull(master);
        if (master.Parts is not { Count: > 0 } parts || master.IsFinal)
            return;
        if (!parts.TrueForAll(p => p.IsFinal))
        {
            if (parts.Exists(p => p.State is TargetState.Building or TargetState.Fighting || p.IsFinal))
                master.State = TargetState.Fighting;
            master.TrialsDone = parts.Sum(p => p.TrialsDone);
            return;
        }
        master.TrialsDone = parts.Sum(p => p.TrialsDone);
        master.GameSeconds = parts.Sum(p => p.GameSeconds);
        master.RealSeconds = parts.Sum(p => p.RealSeconds);
        master.Unsupported = [.. parts.SelectMany(p => p.Unsupported).DistinctBy(n => n.English())];
        master.NotSimulated = GapNote.Merged(parts);
        if (parts.Find(p => p.State == TargetState.Cancelled) is { } cancelled)
        {
            master.State = TargetState.Cancelled;
            master.Reasons = [.. cancelled.Reasons];
            return;
        }
        // Its monsters are refused only for the master's own lock, so all of them are.
        if (parts.TrueForAll(p => p.State == TargetState.Refused))
        {
            master.State = TargetState.Refused;
            master.Reasons = [.. parts[0].Reasons];
            return;
        }
        // Every fight's own notes are the master's list; what a monster's fights add (requirements ignored) is kept.
        var common = Assumption.All.Select(a => a.Note.English()).ToHashSet(StringComparer.Ordinal);
        master.Assumptions =
        [
            .. Assumption.ForMaster.Select(a => a.Note),
            .. master.Loadouts is { Count: > 0 } ? [Assumption.FreeSwaps.Note] : Array.Empty<Note>(),
            .. parts
                .SelectMany(p => p.Assumptions)
                .Where(n => !common.Contains(n.English()))
                .DistinctBy(n => n.English(), StringComparer.Ordinal),
        ];
        master.State = TargetState.Finished;
    }

    /// <summary>Ends a master and every monster of it not yet final, with the reason.</summary>
    public static void End(TargetResult master, TargetState state, Note reason)
    {
        ArgumentNullException.ThrowIfNull(master);
        foreach (var part in master.Parts?.Where(p => !p.IsFinal) ?? [])
        {
            part.State = state;
            part.Reasons = [reason];
            part.Batches.Clear();
        }
        master.State = state;
        master.Reasons = [reason];
    }
}
