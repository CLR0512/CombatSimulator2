using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

internal sealed partial class RunService
{
    /// <summary>Shows a run loaded from disk when nothing has run yet.</summary>
    public void Show(Run run)
    {
        if (IsActive)
            return;
        Current = run;
        _changed = true;
    }

    /// <summary>Shows a comparison loaded from disk when nothing has run yet.</summary>
    public void Show(ComparedRuns comparison)
    {
        if (IsActive)
            return;
        Comparison = comparison;
        _changed = true;
    }

    /// <summary>Main thread: names a kept run; a blank name clears it. Returns its header.</summary>
    public RunHeader RenameRun(string id, string? name)
    {
        var clean = RunHistory.CleanName(name);
        return UpdateSaved(id, run => run.Name = clean);
    }

    /// <summary>Main thread: pins a kept run past the keep limit, or unpins it. Returns its header.</summary>
    public RunHeader PinRun(string id, bool pinned) => UpdateSaved(id, run => run.Pinned = pinned);

    // Changes a kept run on disk, and the same run where it is on screen.
    private RunHeader UpdateSaved(string id, Action<Run> change)
    {
        MainThread.Assert("RunService.UpdateSaved");
        if (Current is { IsFinal: false } active && active.Id == id)
            throw new CommandException(Note.Of("the run is in progress").English());
        RunHeader header;
        try
        {
            header = RunStore.Shared.Update(id, change);
        }
        catch (Exception e) when (e is RunFileException or ArgumentException)
        {
            throw new CommandException($"no saved run '{id}' ({e.Message})");
        }
        foreach (var shown in OnScreen().Where(r => r.Id == id))
            change(shown);
        _changed = true;
        return header;
    }

    /// <summary>Why the kept run may not be deleted (in progress, pinned, or a kept comparison's), or null.</summary>
    public Note? DeleteRefusal(RunHeader header) =>
        RunHistory.CanDelete(header, Current is { IsFinal: false } run ? run.Id : null, RunStore.Shared.Pinned());

    /// <summary>
    /// Main thread: deletes a kept run, or an unreadable file by its file name. Returns why it was refused, or null
    /// once it is gone. The shown run, if it was this one, gives way to the newest remaining one, or to none.
    /// </summary>
    public Note? DeleteRun(string idOrFile)
    {
        MainThread.Assert("RunService.DeleteRun");
        var store = RunStore.Shared;
        var (headers, unreadable) = store.ListAll();
        if (headers.FirstOrDefault(h => h.Id == idOrFile) is not { } header)
        {
            if (Current is { IsFinal: false } active && active.Id == idOrFile)
                return Note.Of("the run is in progress");
            var file = unreadable.FirstOrDefault(u => u.File == idOrFile || u.File == idOrFile + RunFile.Extension);
            if (file == null)
                throw new CommandException($"no saved run '{idOrFile}' (see cs2 runs)");
            store.Delete(file.File);
            _changed = true;
            return null;
        }
        if (DeleteRefusal(header) is { } refusal)
            return refusal;
        store.Delete(header.Id);
        Results?.Keep(store.List().Select(h => h.Id));
        if (Current?.Id == header.Id)
            Current = store.Newest();
        _changed = true;
        Log.Info($"{header.Id} deleted");
        return null;
    }

    /// <summary>
    /// Main thread: makes a kept run the shown result. Refused while a run or comparison is in progress. Returns the
    /// comparison's build warnings when the run came from another game or CS2 build, else null.
    /// </summary>
    public IReadOnlyList<Note>? ShowSaved(string id)
    {
        MainThread.Assert("RunService.ShowSaved");
        if (IsActive)
            throw new CommandException("a run is in progress: cancel it first (cs2 cancel)");
        Run run;
        try
        {
            run = RunStore.Shared.Read(id);
        }
        catch (Exception e) when (e is RunFileException or ArgumentException)
        {
            if (e is RunFileException)
                RunFailures.LoadFailed(FailureLog, id, e);
            throw new CommandException($"no saved run '{id}' ({e.Message})");
        }
        Current = run;
        _changed = true;
        return RunHistory.BuildWarning(run, LiveGame.GameBuild, Cs2Mvid);
    }

    // The runs on screen: the shown run and the shown comparison's sides.
    private IEnumerable<Run> OnScreen()
    {
        if (Current != null)
            yield return Current;
        if (Comparison is { } comparison)
        {
            yield return comparison.A;
            yield return comparison.B;
        }
    }

    /// <summary>The runs a comparison needs: those of every kept one and of the one on screen.</summary>
    public IReadOnlySet<string> ComparisonRuns() => Keeper?.RunsWith(Comparison) ?? new HashSet<string>();

    /// <summary>The kept comparisons, newest first, each with its runs' headers.</summary>
    public IReadOnlyList<KeptComparison> KeptComparisons => Keeper?.Entries() ?? [];

    /// <summary>
    /// Main thread: shows a kept comparison as it finished: no run starts, and the sides being set up stay as they
    /// are. Refused while a run is in progress, and for a comparison with a run that is gone.
    /// </summary>
    public ComparedRuns ShowKept(string id)
    {
        MainThread.Assert("RunService.ShowKept");
        if (IsActive)
            throw new CommandException("a run is in progress: cancel it first (cs2 cancel)");
        Comparison = Keeper!.Reopen(id);
        Current = Comparison.B;
        _changed = true;
        return Comparison;
    }

    /// <summary>Main thread: stops keeping a comparison, its runs staying saved; false when none has that id.</summary>
    public bool RemoveKept(string id)
    {
        var removed = Keeper?.Remove([id]) > 0;
        _changed |= removed;
        return removed;
    }

    /// <summary>What clearing the saved runs would do now; what is in progress is left out of it.</summary>
    public ClearPlan ClearPlan() =>
        Keeper!.Plan(OnScreen().Where(r => !r.IsFinal || _pendingB != null).Select(r => r.Id));

    /// <summary>Main thread: clears as <paramref name="plan"/> says; what is on screen leaves only if its runs went.</summary>
    public void Clear(ClearPlan plan)
    {
        MainThread.Assert("RunService.Clear");
        Keeper!.Clear(plan);
        Results?.Keep(RunStore.Shared.List().Select(h => h.Id));
        if (Comparison is { IsFinal: true } shown && (plan.Deletes(shown.A.Id) || plan.Deletes(shown.B.Id)))
            Comparison = null;
        if (Current is { IsFinal: true } current && plan.Deletes(current.Id))
            Current = RunStore.Shared.Newest();
        _changed = true;
    }
}
