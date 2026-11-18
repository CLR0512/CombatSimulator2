namespace MelvorMods.CombatSimulator2.Model;

/// <summary>A file in the run folder that can't be read as a run, and why.</summary>
public sealed record UnreadableRun(string File, string Why);

/// <summary>
/// Saved runs: one file per run in a folder, at most <see cref="Max"/> besides the pinned ones (pinned in their
/// header, or named by <see cref="Pinned"/>); writing a new one deletes the oldest unpinned by end time. Unreadable
/// files and files from another format are skipped and reported, never fatal. Thread-safe.
/// </summary>
public sealed class RunStore(string folder, int max = RunStore.DefaultMax)
{
    public const int DefaultMax = 20;
    public const int MinLimit = 5;
    public const int MaxLimit = 100;

    /// <summary>The live game's store, set by the mod when it loads.</summary>
    public static RunStore Shared { get; set; } = null!;

    private readonly Lock _lock = new();
    private long _changes;

    /// <summary>
    /// Moves whenever the saved runs change: a run written, changed (renamed, pinned, unpinned) or deleted. A list
    /// built at one value still says the same while the value stays.
    /// </summary>
    public long Changes => Interlocked.Read(ref _changes);
    private List<string> _skipped = [];
    private int _max = Math.Clamp(max, MinLimit, MaxLimit);

    public string Folder { get; } = folder;

    /// <summary>The runs never pruned however old: the sides of the kept comparisons and of the one on screen.</summary>
    public Func<IReadOnlySet<string>> Pinned { get; set; } = () => new HashSet<string>();

    /// <summary>
    /// How many runs are kept, <see cref="MinLimit"/>…<see cref="MaxLimit"/>; a lower limit applies at the next write.
    /// </summary>
    public int Max
    {
        get
        {
            lock (_lock)
                return _max;
        }
        set
        {
            lock (_lock)
                _max = Math.Clamp(value, MinLimit, MaxLimit);
        }
    }

    /// <summary>The bytes used by the run files, readable or not.</summary>
    public long SizeOnDisk
    {
        get
        {
            lock (_lock)
                return RunFiles().Sum(path => new FileInfo(path).Length);
        }
    }

    /// <summary>
    /// Deletes exactly the runs <paramref name="plan"/> names, and the run files that can't be read; every other
    /// file stays, a run saved since the plan was made too. Returns how many files were deleted.
    /// </summary>
    public int Clear(ClearPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_lock)
        {
            var files = plan
                .Delete.Select(PathOf)
                .Concat(ListLocked().Unreadable.Select(u => Path.Combine(Folder, u.File)))
                .Where(File.Exists)
                .ToList();
            foreach (var path in files)
                File.Delete(path);
            var deleted = files.Count;
            _skipped = [];
            Interlocked.Increment(ref _changes);
            return deleted;
        }
    }

    /// <summary>The files skipped by the last listing, with why.</summary>
    public IReadOnlyList<string> Skipped
    {
        get
        {
            lock (_lock)
                return [.. _skipped];
        }
    }

    public void Write(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (_lock)
        {
            Directory.CreateDirectory(Folder);
            RunFile.Write(PathOf(run.Id), run);
            var headers = ListLocked().Headers;
            var pinned = Pinned();
            foreach (var old in headers.Skip(_max).Where(h => !h.Pinned && !pinned.Contains(h.Id)))
                File.Delete(PathOf(old.Id));
            Interlocked.Increment(ref _changes);
        }
    }

    /// <summary>The saved runs, newest first by end time, from their headers only.</summary>
    public IReadOnlyList<RunHeader> List()
    {
        lock (_lock)
            return ListLocked().Headers;
    }

    /// <summary>
    /// The saved runs as <see cref="List"/> gives them, and the files that couldn't be read, with why.
    /// </summary>
    public (IReadOnlyList<RunHeader> Headers, IReadOnlyList<UnreadableRun> Unreadable) ListAll()
    {
        lock (_lock)
            return ListLocked();
    }

    public Run Read(string id)
    {
        lock (_lock)
            return RunFile.Read(PathOf(id));
    }

    /// <summary>Whether a run with that id is on disk.</summary>
    public bool Has(string id)
    {
        lock (_lock)
            return File.Exists(PathOf(id));
    }

    /// <summary>The character a saved run was simulated for; null when the run is gone or doesn't say.</summary>
    public string? CharacterOf(string id)
    {
        lock (_lock)
            return File.Exists(PathOf(id)) ? RunFile.ReadCharacter(PathOf(id)) : null;
    }

    /// <summary>
    /// Reads a run, changes it and writes it back in place (atomically). Its id and end time stay, so its place in
    /// the list doesn't move. Returns its new header; throws <see cref="RunFileException"/> when it can't be read.
    /// </summary>
    public RunHeader Update(string id, Action<Run> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_lock)
        {
            var path = PathOf(id);
            var run = RunFile.Read(path);
            var (keptId, endedAt) = (run.Id, run.EndedAt);
            change(run);
            run.Id = keptId;
            run.EndedAt = endedAt;
            RunFile.Write(path, run);
            Interlocked.Increment(ref _changes);
            return RunHeader.Of(run);
        }
    }

    /// <summary>
    /// Deletes one run by id, or an unreadable file by its file name (ending in <see cref="RunFile.Extension"/>);
    /// true when it was there. Only files in the folder can be named.
    /// </summary>
    public bool Delete(string idOrFile)
    {
        ArgumentNullException.ThrowIfNull(idOrFile);
        lock (_lock)
        {
            var path = idOrFile.EndsWith(RunFile.Extension, StringComparison.Ordinal)
                ? Path.Combine(Folder, Checked(idOrFile))
                : PathOf(idOrFile);
            if (!File.Exists(path))
                return false;
            File.Delete(path);
            Interlocked.Increment(ref _changes);
            return true;
        }
    }

    /// <summary>The newest readable run, or null.</summary>
    public Run? Newest()
    {
        foreach (var header in List())
        {
            try
            {
                return Read(header.Id);
            }
            catch (RunFileException e)
            {
                lock (_lock)
                    _skipped.Add(e.Message);
            }
        }
        return null;
    }

    private (IReadOnlyList<RunHeader> Headers, IReadOnlyList<UnreadableRun> Unreadable) ListLocked()
    {
        var unreadable = new List<UnreadableRun>();
        var headers = new List<RunHeader>();
        foreach (var path in RunFiles())
        {
            try
            {
                headers.Add(RunFile.ReadHeader(path));
            }
            catch (RunFileException e)
            {
                unreadable.Add(new UnreadableRun(Path.GetFileName(path), e.Message));
            }
        }
        _skipped = [.. unreadable.Select(u => u.Why)];
        return (
            [
                .. headers
                    .OrderByDescending(h => h.EndedAt ?? h.StartedAt)
                    .ThenByDescending(h => h.Id, StringComparer.Ordinal),
            ],
            unreadable
        );
    }

    private IEnumerable<string> RunFiles() =>
        Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "*" + RunFile.Extension) : [];

    private string PathOf(string id) => Path.Combine(Folder, Checked(id + RunFile.Extension));

    // A bare file name: ids come from the bridge, and must never reach outside the folder.
    private static string Checked(string file) =>
        file.Length > 0 && Path.GetFileName(file) == file && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            ? file
            : throw new ArgumentException($"'{file}' is not a run file name", nameof(file));
}
