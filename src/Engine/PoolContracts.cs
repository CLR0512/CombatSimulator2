using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// What a pool run needs: the setup's snapshot, loadout and changes, the targets and how to fight them. With
/// <paramref name="Reuse"/>, a copy fights target after target, reset between them; without, each target gets a
/// fresh copy. Each target in <paramref name="PartLoadouts"/> (a master's monster given a loadout of its own) wears
/// that loadout for its fight.
/// </summary>
internal sealed record PoolJob(
    Snapshot Snapshot,
    string? LoadoutSlot,
    IReadOnlyList<TargetSpec> Targets,
    DriveOptions Options,
    ulong Seed,
    int Copies,
    bool Reuse = true,
    IReadOnlyDictionary<string, ulong>? TargetSeeds = null,
    int NextRunCopies = 0,
    ChangeList? Changes = null,
    string? RunId = null,
    char? Side = null,
    IReadOnlyDictionary<string, string>? PartLoadouts = null
)
{
    /// <summary>The loadout slot <paramref name="key"/> wears for its fight; null for the job's own setup.</summary>
    public string? LoadoutOf(string key) => PartLoadouts?.GetValueOrDefault(key);

    /// <summary>The run seed <paramref name="key"/> is fought with: its own when it has one, else the job's.</summary>
    public ulong SeedFor(string key) => TargetSeeds != null && TargetSeeds.TryGetValue(key, out var seed) ? seed : Seed;
}

/// <summary>Worker output, called on worker threads.</summary>
internal interface IPoolReport
{
    void State(string key, TargetState state);

    /// <summary>The setup's main skill, read from the first prepared copy.</summary>
    void MainSkill(string? skill);

    /// <summary>
    /// A fighting target's trials done so far, at most every <see cref="CopyPool.ProgressInterval"/>.
    /// </summary>
    void Progress(string key, int trialsDone);
    void Done(TargetResult result);

    /// <summary>A copy failed to build or prepare and was dropped: <paramref name="error"/> is its type and message.</summary>
    void CopyDropped(string error);
    void Finished(PoolStats stats);
    void Failed(Exception error);
}

/// <summary>A pool run in another process (the simulator host): the same job in, the same report calls out.</summary>
internal interface IRemotePool
{
    void Start(PoolJob job, IPoolReport report, int generation, CancellationToken token);

    /// <summary>
    /// Starts the host if needed and has it build <paramref name="stockTarget"/> bare copies; returns at once.
    /// </summary>
    void Warm(int stockTarget);

    /// <summary>Has the host drop its bare copies; returns at once.</summary>
    void Release();

    /// <summary>The panel opened or closed: a host with the panel closed is stopped sooner.</summary>
    void Panel(bool open);

    /// <summary>A search's stages in the host, which keeps the search's copies between them until it is disposed.</summary>
    Search.ISearchFighter NewSearch();
}

/// <summary>
/// The simulator host as the engine and the panel see it: its state, why it failed, and its last reading.
/// </summary>
internal interface IHostStatus
{
    HostState State { get; }
    Note? Reason { get; }
    int? Pid { get; }

    /// <summary>The host's last heartbeat, if it has sent one.</summary>
    HostReading? Reading { get; }

    /// <summary>The host dumps kept, newest first, after pruning, and their folder; no folder when there is none.</summary>
    (string? Folder, IReadOnlyList<HostDumpInfo> Dumps) KeptDumps();
}

/// <summary>
/// One heartbeat from the host: its CPU and memory, the machine's free memory, the copies alive, its warm stock
/// (bare copies ready, the target, and <c>none</c>, <c>filling</c> or <c>ready</c>), its managed heap and the active
/// run's own peak (0 when idle).
/// </summary>
internal sealed record HostReading(
    double CpuPercent,
    long PrivateBytes,
    long PeakPrivateBytes,
    long AvailableBytes,
    int Copies,
    int Stock = 0,
    int StockTarget = 0,
    string Warm = "none",
    long HeapBytes = 0,
    long HeapCommittedBytes = 0,
    long RunPeakPrivateBytes = 0
);

/// <summary>A host problem a run reports as its error, in plain words.</summary>
internal sealed class HostException(Note reason, Exception? inner = null) : Exception(reason.English(), inner)
{
    public Note Reason { get; } = reason;

    /// <summary>The host itself failed (it crashed, hung or couldn't start), rather than one run on it.</summary>
    public bool Lost { get; init; }
}

/// <summary>
/// A copy can't be built without going under the memory reserve, and nothing else is built to wait for.
/// </summary>
internal sealed class NotEnoughMemoryException(Note reason) : Exception(reason.English())
{
    public Note Reason { get; } = reason;
}
