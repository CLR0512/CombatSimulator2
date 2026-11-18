using System.Diagnostics;
using System.Security.Cryptography;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// The one active run, owned by the mod rather than the panel so it outlives the panel. The
/// panel and <c>cs2</c> drive the same runs. All state changes happen on the main thread; workers post through a
/// <see cref="ResultQueue"/> tagged with the run's generation, so output from a cancelled run is dropped.
/// </summary>
internal sealed partial class RunService : IDisposable
{
    private readonly ResultQueue _queue = new();
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cancel;
    private int _generation;
    private char? _side; // the current run's side, when it is one of a comparison's
    private bool _changed;

    public static RunService Instance { get; private set; } = new();

    /// <summary>The run on screen: the active one, or the last one.</summary>
    public Run? Current { get; private set; }

    public bool IsActive =>
        Current is { IsFinal: false }
        || _pendingB != null
        || Search is { IsActive: true }
        || BestLoadouts is { IsActive: true }
        || BestMasters is { IsActive: true };

    /// <summary>The comparison on screen: the active one, or the last one (loaded from disk at start).</summary>
    public ComparedRuns? Comparison { get; private set; }

    /// <summary>The comparisons kept on disk; set by the mod when it loads.</summary>
    internal ComparisonKeeper? Keeper { get; set; }

    public PoolStats? LastStats { get; private set; }

    /// <summary>Copies fight target after target, reset between them (on by default; <c>cs2dev reuse off</c>).</summary>
    public static bool ReuseCopies { get; private set; } = true;

    private double _lastResultAt;

    /// <summary>
    /// Raised on the main thread at most once per frame, after results arrive or the run's state changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Raised on the main thread when a run becomes final (finished or cancelled) and should be saved, and when a
    /// comparison's two sides are final (again when it is swapped).
    /// </summary>
    public event Action<Run>? Finalised;
    public event Action<ComparedRuns>? Compared;

    /// <summary>Ids already used, so a new run id is unique across restarts.</summary>
    public Func<IEnumerable<string>> KnownIds { get; set; } = () => [];

    public const string InHost = "host";
    public const string InGame = "game";

    /// <summary>Where runs simulate when a request doesn't say: the player's setting.</summary>
    public static Func<string> DefaultWhere { get; set; } = () => InHost;

    private ProcessMeter? _gameMeter;
    private long _gameReadAt = long.MinValue;
    private (double CpuPercent, long PrivateBytes, long PeakPrivateBytes) _gameReading;

    /// <summary>
    /// What the panel shows besides results: copies, performance and the simulator host, as of now. The game's own
    /// CPU is read at most once a second, so its figure covers a whole second. Main thread.
    /// </summary>
    public RunStatus Status
    {
        get
        {
            var now = Environment.TickCount64;
            if (now - _gameReadAt >= 1000)
            {
                _gameReadAt = now;
                _gameReading = (_gameMeter ??= new ProcessMeter()).Read();
            }
            var host = HostStatus?.Invoke();
            return new RunStatus(
                Engine.DefaultCopies.Now,
                PerfRecorder.Current,
                PerfRecorder.Last,
                PerfRecorder.SearchCurrent,
                PerfRecorder.SearchLast,
                _gameReading,
                MemoryReserve.Available(),
                host?.State ?? HostState.NotStarted,
                host?.Reason,
                host?.Reading
            );
        }
    }

    /// <summary>
    /// The simulator host alone, without the rest of <see cref="Status"/>: the panel looks at it a few times a second,
    /// and building the whole status (and reading the game's process) each time would allocate on an idle frame.
    /// </summary>
    public static IHostStatus? Host => HostStatus?.Invoke();

    /// <summary>Set by the mod: finished results of saved and session runs, for reuse.</summary>
    public static ResultIndex? Results { get; set; }

    /// <summary>This CS2 build: results simulated by another are never reused.</summary>
    public static string Cs2Mvid { get; } = typeof(RunService).Assembly.ManifestModule.ModuleVersionId.ToString();

    /// <summary>Set by the mod: starts a pool run in the simulator host.</summary>
    public static IRemotePool? Remote { get; set; }

    /// <summary>
    /// Set by the mod: the simulator host's state (the host link is made on first use, once the game is up).
    /// </summary>
    public static Func<IHostStatus>? HostStatus { get; set; }

    /// <summary>
    /// How long after the panel closes, with no run, the warm stock is dropped (<c>cs2dev host release=</c>).
    /// </summary>
    public static TimeSpan ReleaseDelay { get; private set; } = TimeSpan.FromSeconds(60);

    private Timer? _release;

    /// <summary>The panel opened: a run is likely, so the host starts and builds bare copies (host runs only).</summary>
    public void Warm()
    {
        _release?.Dispose();
        _release = null;
        Remote?.Panel(open: true);
        if (DefaultWhere() != InHost || Remote is not { } remote)
            return;
        remote.Warm(Engine.DefaultCopies.Stock);
    }

    /// <summary>
    /// The panel closed: drop the host's bare copies after <see cref="ReleaseDelay"/>, unless it opens again.
    /// </summary>
    public void PanelClosed()
    {
        if (Remote is not { } remote)
            return;
        remote.Panel(open: false);
        _release?.Dispose();
        _release = new Timer(_ => remote.Release(), null, ReleaseDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>On unload, replaces the service with a fresh one for the next load.</summary>
    public static void Reset()
    {
        Instance.Dispose();
        Instance = new RunService();
    }

    public void Dispose()
    {
        _cancel?.Dispose();
        _release?.Dispose();
        Search?.Dispose();
    }

    /// <summary>
    /// Starts a run: snapshot on the main thread now, then the pool on its own threads.
    /// </summary>
    public Run Start(RunRequest request, Snapshot? snapshotTaken = null)
    {
        MainThread.Assert("RunService.Start");
        if (IsStarting)
            throw new CommandException("a run is starting: wait for it");
        if (Search is { IsActive: true })
            throw new CommandException("a gear search is still running: cancel it first (cs2 gear cancel)");
        if (BestLoadouts is { IsActive: true })
            throw new CommandException("Best loadouts is running: cancel it first (cs2 loadouts best cancel)");
        if (IsActive)
            throw new CommandException($"{Current!.Id} is still running: cancel it first (cs2 cancel)");
        if (request.Targets.Count == 0)
            throw new CommandException("no targets to run");
        var game = LiveGame.Require();
        var deps = Dependencies.Resolve(game);
        var required = deps.Missing.Where(d => Dependencies.Required.Contains(d.Feature)).ToList();
        if (required.Count > 0)
            throw new CommandException("can't simulate on this game build, missing: " + string.Join(", ", required));
        if (request.Targets.Any(t => t.Kind == TargetKind.Master) && !deps.IsAvailable(Dependencies.SlayerMasters))
            throw new CommandException("Slayer masters are unsupported on this game build (see cs2 deps)");
        if (request.Setup.Loadout != Setup.Current && !deps.IsAvailable(Dependencies.Loadouts))
            throw new CommandException("loadouts are unsupported on this game build (see cs2 deps)");
        var slot = CheckLoadout(request.Setup, () => LiveGame.Loadouts(game));
        var snapshot = snapshotTaken ?? LiveGame.TakeSnapshot(game);
        var seed = request.Seed ?? NewSeed();
        var where = request.Where ?? DefaultWhere();
        var known = KnownIds().Append(Current?.Id ?? "");
        var run = RunPlan.NewRun(
            request,
            snapshot,
            RunIds.Next(DateTime.Now, known),
            seed,
            where,
            target => LiveGame.TargetName(game, target, masked: false)
        );
        using var scope = LogScope.Push(run: run.Id, side: request.Side, phase: RunPhase.Plan);
        var lookup = Stopwatch.StartNew();
        run.SetupHash = Fingerprints.SetupHash(game);
        run.Cs2Mvid = Cs2Mvid;
        var runnable = request.Targets.ToList();
        var simulate = request.Fresh
            ? runnable
            : ResultReuse.Reuse(run, runnable, key => ResultReuse.SeedOf(request, key));
        run.Timings!.ReuseLookup = lookup.Elapsed.TotalSeconds;
        run.Timings.Reused = runnable.Count - simulate.Count;
        simulate = WithMasterParts(
            run,
            simulate,
            master => SlayerTasks.Read(game, master.Master!, run.Setup.Changes),
            target => LiveGame.TargetName(game, target, masked: false),
            [.. LiveGame.Loadouts(game).Where(l => l.Unlocked).Select(l => l.Slot)]
        );
        simulate = RunPlan.WithSlayerTasks(
            simulate,
            run.Setup.Changes,
            target => SlayerTasks.MasterFor(game, target, run.Setup.Changes)
        );
        GuardLog.Reset();
        _queue.Clear();
        _cancel?.Dispose(); // the previous run is final, and a cancelled token reads the same after disposal
        _cancel = new CancellationTokenSource();
        var generation = ++_generation;
        Current = run;
        _snapshot = snapshot;
        _side = request.Side;
        LastStats = null;
        _changed = true;
        _clock.Restart();
        _lastResultAt = 0;
        if (run.Timings.Reused > 0)
            ResultArrived(run);
        if (simulate.Count == 0)
        {
            // Every target answered from earlier runs: final at once, nothing to start.
            Log.Info($"{run.Id} answered from earlier runs: {run.Targets.Count} targets");
            Finish(run, RunState.Finished);
            return run;
        }
        var job = RunPlan.Job(
            request,
            snapshot,
            slot,
            RunPlan.LongestFirst(simulate, Results),
            seed,
            run.Limits,
            ReuseCopies,
            run.Id
        );
        if (where == InHost)
            (Remote ?? throw new InvalidOperationException("no simulator host")).Start(
                job,
                new Report(this, generation, run.Id, request.Side),
                generation,
                _cancel.Token
            );
        else
            CopyPool.Start(job, new Report(this, generation, run.Id, request.Side), _cancel.Token);
        Log.Info(
            $"{run.Id} started in {where}: {run.Targets.Count} targets, seed {seed}, copies {request.Copies}, setup {run.Setup.Identity}, snapshot {snapshot.TakeMs:0.0} ms"
        );
        return run;
    }

    // A master left to simulate is fought as its monsters: they replace it in what the pool is given.
    private static List<TargetSpec> WithMasterParts(
        Run run,
        List<TargetSpec> simulate,
        Func<TargetSpec, MasterSpec?> read,
        Func<TargetSpec, string> name,
        IReadOnlyCollection<string> usableLoadouts
    )
    {
        var masters = simulate.Where(t => t.Kind == TargetKind.Master).ToList();
        if (masters.Count == 0)
            return simulate;
        var parts = RunPlan.Expand(
            run,
            masters.ToDictionary(t => t.Key, read, StringComparer.Ordinal),
            name,
            usableLoadouts
        );
        return [.. simulate.Where(t => t.Kind != TargetKind.Master), .. parts];
    }

    /// <summary>
    /// Cancels the active run, which becomes final for the player at once; workers stop at their next check, builds
    /// in flight finish detached and are disposed, and their late output is dropped by generation.
    /// </summary>
    public (Run Run, double Ms, int Finished, int Cancelled) Cancel(Note? reason = null) =>
        End(reason ?? Note.Of("cancelled"), RunState.Cancelled);

    /// <summary>Ends the run as an error: like a cancel, with the reason kept on the run.</summary>
    private void Fail(Note reason)
    {
        if (Current is { IsFinal: false } run)
            run.Error = reason;
        End(reason, RunState.Error);
    }

    private (Run Run, double Ms, int Finished, int Cancelled) End(Note reason, RunState state)
    {
        MainThread.Assert("RunService.Cancel");
        var watch = Stopwatch.StartNew();
        if (Current is not { IsFinal: false } && _pendingB != null && Comparison is { } waiting)
        {
            // Between the sides: side B hasn't started, so it ends with nothing simulated.
            _pendingB = null;
            ComparedRuns.EndPlaceholder(waiting.B, reason, state);
            ComparisonDone(waiting);
            return (waiting.B, watch.Elapsed.TotalMilliseconds, 0, waiting.B.Targets.Count);
        }
        var run = Current is { IsFinal: false } active ? active : throw new CommandException("no run is active");
        using var scope = ScopeOf(run);
        _cancel?.Cancel();
        _generation++;
        _queue.Clear();
        var cancelled = 0;
        foreach (var result in run.Results.Values.Where(r => !r.IsFinal))
        {
            if (result.Parts != null)
            {
                MasterParts.End(result, TargetState.Cancelled, reason);
                cancelled++;
                continue;
            }
            result.State = TargetState.Cancelled;
            result.Reasons.Clear();
            result.Reasons.Add(reason);
            result.Batches.Clear();
            result.Fights.Clear();
            cancelled++;
        }
        Finish(run, state);
        var finished = run.Results.Values.Count(r => r.State == TargetState.Finished);
        return (run, watch.Elapsed.TotalMilliseconds, finished, cancelled);
    }

    /// <summary>Real seconds since the current run started.</summary>
    public double RealSeconds => _clock.Elapsed.TotalSeconds;

    private void Finish(Run run, RunState state)
    {
        using var scope = ScopeOf(run, RunPhase.Report);
        run.State = state;
        run.EndedAt = DateTime.UtcNow;
        _clock.Stop();
        if (run.Timings is { } timings)
        {
            timings.Total = _clock.Elapsed.TotalSeconds;
            timings.Report = _lastResultAt > 0 ? timings.Total - _lastResultAt : 0;
        }
        _changed = true;
        Log.Info($"{run.Id} {state.ToString().ToLowerInvariant()} in {RealSeconds:0.0} s");
        RunFailures.TargetsFailed(FailureLog, run, SideOf(run));
        KeepSnapshotIfFailed(run);
        Finalised?.Invoke(run);
        if (Comparison is not { } comparison)
            return;
        if (ReferenceEquals(run, comparison.B))
            ComparisonDone(comparison);
        else if (ReferenceEquals(run, comparison.A) && _pendingB != null && state != RunState.Finished)
            StartSideB(); // A was cancelled or failed: B ends without starting
    }

    // The run's id and side on the lines written while it starts, ends or is saved.
    private LogScope.Restore ScopeOf(Run run, RunPhase? phase = null) =>
        LogScope.Push(run: run.Id, side: SideOf(run), phase: phase);

    private char? SideOf(Run run) =>
        ReferenceEquals(run, Current) ? _side
        : Comparison is not { } comparison ? null
        : ReferenceEquals(run, comparison.A) ? 'A'
        : ReferenceEquals(run, comparison.B) ? 'B'
        : null;

    internal static ulong NewSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes) >> 11; // up to 2^53, so it reads back exactly from JSON tools
    }

    // A loadout must exist in the live game and be unlocked when the run starts.
    internal static string? CheckLoadout(Setup setup, Func<IReadOnlyList<LoadoutInfo>> loadouts)
    {
        if (setup.Loadout == Setup.Current)
            return null;
        var loadout = loadouts().FirstOrDefault(l => l.Slot == setup.Loadout);
        if (loadout == null)
            throw new CommandException($"no loadout '{Ids.Brief(setup.Loadout)}' (see cs2 setups)");
        if (!loadout.Unlocked)
            throw new CommandException($"loadout '{loadout.Name}' is locked");
        setup.LoadoutName = loadout.Name;
        return loadout.Slot;
    }
}
