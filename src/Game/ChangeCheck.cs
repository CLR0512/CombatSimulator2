using System.Diagnostics;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// Changes as checked on a check copy: each with what became of it, the base's state before them
/// (<see cref="Before"/>), the side's state and gear after them (<see cref="After"/>, <see cref="Gear"/>), how long
/// the check took and how long applying the changes took in it. <c>Setup</c> is the base's identity.
/// </summary>
internal sealed record ChangeCheckResult(
    string Setup,
    ChangeList Changes,
    IReadOnlyList<ChangeOutcome> Outcomes,
    SetupState Before,
    SetupState After,
    SetupGear Gear,
    double ElapsedMs,
    double ApplyMs
)
{
    /// <summary>The changes that apply: the list the side runs with.</summary>
    public ChangeList Applied => ChangeOutcome.Applied(Outcomes);
}

/// <summary>
/// One check of a side: its changes as tried on its base, the stats and modifiers of the setup they leave, its
/// setup pages, and the whole setup as a setup line's entries, all read from the same copy.
/// </summary>
internal sealed record SideCheck(
    ChangeCheckResult Changes,
    SetupSheet Sheet,
    IReadOnlyList<SetupPage> Pages,
    IReadOnlyList<LineEntry> Line
);

/// <summary>
/// Checks a side before anything runs, as <see cref="TargetCatalog.Check"/> checks targets: a snapshot on the main
/// thread, then a check copy on a background thread prepared with the side's base and given its changes. The copy
/// is put back as prepared and kept for the next check of the same base, so edits that follow each other don't wait
/// for a copy; a spare bare copy is built for the first check of another base. Both are dropped after a minute
/// unused.
/// </summary>
internal static class ChangeCheck
{
    private static readonly TimeSpan SpareKept = TimeSpan.FromSeconds(60);
    private static readonly Lock _lock = new();
    private static Copy? _spare;
    private static bool _building;
    private static Timer? _drop;

    // The check copy as prepared for one base: the snapshot it was read from, the loadout swapped to, and how to
    // put it back.
    private static (string Snapshot, string? Loadout, Copy Copy, CopyUndo Undo)? _prepared;

    /// <summary>
    /// Snapshots the live game on the main thread and starts the check of <paramref name="changes"/> on
    /// <paramref name="setup"/> (the side's base and requirements flag; its own changes are not read);
    /// <paramref name="done"/> runs on the main thread with the result or the error. <paramref name="setupHash"/>
    /// goes on the sheet.
    /// </summary>
    public static void Side(
        MelvorGame game,
        Setup setup,
        string? loadoutSlot,
        ChangeList changes,
        string? setupHash,
        Action<SideCheck?, Exception?> done
    )
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(changes);
        MainThread.Assert("ChangeCheck.Side");
        var watch = Stopwatch.StartNew();
        var snapshot = LiveGame.TakeSnapshot(game);
        var character = setup.Character.Length > 0 ? setup.Character : snapshot.Character;
        var onBase = new Setup
        {
            Character = character,
            Loadout = setup.Loadout,
            LoadoutName = setup.LoadoutName,
            IgnoreRequirements = setup.IgnoreRequirements,
        };
        var ignore = setup.IgnoreRequirements;
        var thread = new Thread(() =>
        {
            try
            {
                SharedCaches.Fill();
                Copy copy;
                CopyUndo undo;
                if (TakePrepared(snapshot.Sha256, loadoutSlot) is { } kept)
                    (copy, undo) = kept;
                else
                {
                    copy = TakeSpare() ?? Copy.Build();
                    BuildSpare();
                    try
                    {
                        copy.Prepare(snapshot, loadoutSlot);
                        copy.KeepPristine();
                        undo = CopyUndo.Of(copy);
                    }
                    catch
                    {
                        copy.Dispose();
                        throw;
                    }
                }
                SideCheck result;
                try
                {
                    SetupState before;
                    SetupState after;
                    List<ChangeOutcome> outcomes;
                    SetupSheet sheet;
                    List<SetupPage> pages;
                    List<LineEntry> line;
                    var apply = new Stopwatch();
                    using (Guards.Enter())
                    {
                        before = SetupState.Read(copy.Game, copy.Combat);
                        var saved = Pages.PageReader.Values(copy.Game);
                        apply.Start();
                        outcomes = Changes.Apply(copy, changes, ignore, throwOnRefusal: false);
                        apply.Stop();
                        after = SetupState.Read(copy.Game, copy.Combat);
                        var changed = new Setup
                        {
                            Character = character,
                            Loadout = onBase.Loadout,
                            LoadoutName = onBase.LoadoutName,
                            IgnoreRequirements = ignore,
                            Changes = ChangeOutcome.Applied(outcomes),
                        };
                        sheet = SetupSheets.Read(copy.Game, copy.Combat, changed, setupHash);
                        pages = Pages.PageReader.Read(copy.Game, saved);
                        line = LineIds.Setup(copy.Game, copy.Combat, changed.Changes);
                    }
                    var gear = copy.ReadGear();
                    result = new SideCheck(
                        new ChangeCheckResult(
                            onBase.Identity,
                            changes,
                            outcomes,
                            before,
                            after,
                            gear,
                            watch.Elapsed.TotalMilliseconds,
                            apply.Elapsed.TotalMilliseconds
                        ),
                        sheet,
                        pages,
                        line
                    );
                }
                catch
                {
                    copy.Dispose();
                    throw;
                }
                PutBack(snapshot.Sha256, loadoutSlot, copy, undo);
                MainThread.Post(() => done(result, null));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                MainThread.Post(() => done(null, e));
            }
        })
        {
            IsBackground = true,
            Name = "cs2-check",
        };
        thread.Start();
    }

    /// <summary>
    /// Reads something from the check copy of <paramref name="snapshot"/>'s base with <paramref name="changes"/>
    /// applied, on a background thread, and puts the copy back as prepared. <paramref name="done"/> runs on the
    /// main thread with what <paramref name="read"/> returned, or the error.
    /// </summary>
    public static void Read<T>(
        Snapshot snapshot,
        string? loadoutSlot,
        ChangeList? changes,
        bool ignoreRequirements,
        Func<Copy, T> read,
        Action<T?, Exception?> done
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var thread = new Thread(() =>
        {
            try
            {
                SharedCaches.Fill();
                Copy copy;
                CopyUndo undo;
                if (TakePrepared(snapshot.Sha256, loadoutSlot) is { } kept)
                    (copy, undo) = kept;
                else
                {
                    copy = TakeSpare() ?? Copy.Build();
                    BuildSpare();
                    try
                    {
                        copy.Prepare(snapshot, loadoutSlot);
                        copy.KeepPristine();
                        undo = CopyUndo.Of(copy);
                    }
                    catch
                    {
                        copy.Dispose();
                        throw;
                    }
                }
                T result;
                try
                {
                    if (changes is { IsEmpty: false })
                        copy.Change(changes, ignoreRequirements);
                    result = read(copy);
                }
                catch
                {
                    copy.Dispose();
                    throw;
                }
                PutBack(snapshot.Sha256, loadoutSlot, copy, undo);
                MainThread.Post(() => done(result, null));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                MainThread.Post(() => done(null, e));
            }
        })
        {
            IsBackground = true,
            Name = "cs2-check",
        };
        thread.Start();
    }

    private static (Copy Copy, CopyUndo Undo)? TakePrepared(string snapshot, string? loadout)
    {
        lock (_lock)
        {
            if (_prepared is not { } kept || kept.Snapshot != snapshot || kept.Loadout != loadout)
                return null;
            _prepared = null;
            return (kept.Copy, kept.Undo);
        }
    }

    // Undoes the check's changes and keeps the copy for the next check of the same base. A copy that doesn't come
    // back as prepared is dropped: the next check prepares another.
    private static void PutBack(string snapshot, string? loadout, Copy copy, CopyUndo undo)
    {
        IReadOnlyList<string> differing;
        try
        {
            differing = undo.Undo();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            differing = [$"{e.GetType().Name}: {e.Message}"];
        }
        if (differing.Count > 0)
        {
            Log.Info($"check copy dropped, not as prepared after its changes: {string.Join(", ", differing.Take(5))}");
            copy.Dispose();
            return;
        }
        Copy? replaced;
        lock (_lock)
        {
            replaced = _prepared?.Copy;
            _prepared = (snapshot, loadout, copy, undo);
            _drop?.Dispose();
            _drop = new Timer(_ => Release(), null, SpareKept, Timeout.InfiniteTimeSpan);
        }
        replaced?.Dispose();
    }

    /// <summary>
    /// Snapshots the live game on the main thread, then reads each setup's sheet on a check copy prepared as a run
    /// would prepare it (its loadout, then its changes, with its requirements flag); <paramref name="done"/> runs on
    /// the main thread with the sheets in the order given, or with the error. <paramref name="setupHash"/> goes on
    /// each sheet.
    /// </summary>
    public static void ReadSheets(
        MelvorGame game,
        IReadOnlyList<(Setup Setup, string? LoadoutSlot)> sides,
        string? setupHash,
        Action<IReadOnlyList<SetupSheet>?, Exception?> done
    )
    {
        ArgumentNullException.ThrowIfNull(sides);
        MainThread.Assert("ChangeCheck.ReadSheets");
        var snapshot = LiveGame.TakeSnapshot(game);
        var thread = new Thread(() =>
        {
            try
            {
                SharedCaches.Fill();
                var sheets = new List<SetupSheet>(sides.Count);
                foreach (var (setup, slot) in sides)
                {
                    // A copy is prepared once: its bank can't be read back a second time.
                    using var copy = (sheets.Count == 0 ? TakeSpare() : null) ?? Copy.Build();
                    if (sheets.Count == 0)
                        BuildSpare();
                    copy.Prepare(snapshot, slot, setup.Changes, setup.IgnoreRequirements, throwOnRefusal: false);
                    using (Guards.Enter())
                        sheets.Add(SetupSheets.Read(copy.Game, copy.Combat, setup, setupHash));
                }
                MainThread.Post(() => done(sheets, null));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                MainThread.Post(() => done(null, e));
            }
        })
        {
            IsBackground = true,
            Name = "cs2-sheet",
        };
        thread.Start();
    }

    /// <summary>Drops the copies kept for the next check (on unload, and after a minute unused).</summary>
    public static void Release()
    {
        Copy? spare;
        Copy? prepared;
        lock (_lock)
        {
            spare = _spare;
            _spare = null;
            prepared = _prepared?.Copy;
            _prepared = null;
            _drop?.Dispose();
            _drop = null;
        }
        spare?.Dispose();
        prepared?.Dispose();
    }

    private static Copy? TakeSpare()
    {
        lock (_lock)
        {
            var spare = _spare;
            _spare = null;
            return spare;
        }
    }

    // Builds the next check's copy on its own thread, unless one is kept or being built.
    private static void BuildSpare()
    {
        lock (_lock)
        {
            if (_spare != null || _building)
                return;
            _building = true;
        }
        var thread = new Thread(() =>
        {
            Copy? built = null;
            try
            {
                built = Copy.Build();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Warn($"spare check copy: {e.Message}");
            }
            lock (_lock)
            {
                _building = false;
                if (built != null && _spare == null)
                {
                    _spare = built;
                    built = null;
                    _drop?.Dispose();
                    _drop = new Timer(_ => Release(), null, SpareKept, Timeout.InfiniteTimeSpan);
                }
            }
            built?.Dispose();
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "cs2-check-spare",
        };
        thread.Start();
    }
}
