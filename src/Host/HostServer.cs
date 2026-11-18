using System.Diagnostics;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// The simulator host: CS2 in a headless game instance of its own. It says hello, then runs one pool at a time for
/// the live game and sends its output back, with a heartbeat every second. Nothing it does reaches the player's game.
/// </summary>
internal sealed partial class HostServer : IDisposable
{
    private readonly WorkerContext _context;
    private readonly FrameReader _reader;
    private readonly FrameWriter _writer;
    private readonly RunGate _gate = new();
    private CancellationTokenSource? _cancel;
    private CopyPool? _pool;
    private readonly WarmStock _stock;
    private readonly DevFrameFallback _devFallback = new();
    private readonly RunPeak _peak = new();
    private volatile bool _stopping;

    private HostServer(WorkerContext context)
    {
        _context = context;
        _reader = new FrameReader(context.Channel);
        _writer = new FrameWriter(context.Channel);
        _stock = new WarmStock(() => _gate.Active != null);
    }

    internal static void Run(WorkerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Log.Use(context.Log);
        StartStages.Begin();
        StartStages.Mark("worker");
        LiveGame.SteamBuild = MelvorMods.Core.GameBuilds.ReadSteamBuild(
            Path.GetDirectoryName(Environment.ProcessPath ?? throw new InvalidOperationException("no process path"))
                ?? throw new InvalidOperationException("no game folder")
        );
        Guards.InHost = true;
        Dependencies.UseLevers(Path.GetDirectoryName(context.ConfigPath) ?? context.ModDirectory);
        context.PatchAll();
        StartStages.Mark("patched");
        using var server = new HostServer(context);
        server.Serve();
    }

    public void Dispose() => _cancel?.Dispose();

    private void Serve()
    {
        // A thread CS2 didn't wrap still ends the host, but with its cause in the host's log first.
        System.Runtime.ExceptionServices.ExceptionHandling.SetUnhandledExceptionHandler(error =>
        {
            Log.Error($"unhandled on {Thread.CurrentThread.Name ?? "a thread"}", error);
            return false;
        });
        var identity = ThisProcess.Identity();
        var cs2 = identity.Cs2Mvid;
        HelloCs2(ref cs2);
        Send(
            MessageKind.Hello,
            new Hello(Protocol.Version, identity.Build, identity.GameMvid, cs2, Environment.ProcessId)
        );
        Log.Info($"hello sent: build {identity.Build}, game {identity.GameMvid}, cs2 {cs2}");
        StartStages.Mark("hello");
        Log.Info(GcSettingsLine());
        new Thread(Beat) { IsBackground = true, Name = "cs2-host-heartbeat" }.Start();
        try
        {
            while (_reader.Read() is { } frame)
            {
                switch (frame.Kind)
                {
                    case MessageKind.Start:
                        var received = Stopwatch.StartNew();
                        var start = Protocol.Decode<StartMessage>(frame);
                        var snapshot = _reader.Read() is { Kind: MessageKind.Snapshot } next
                            ? next.Payload
                            : throw new ProtocolException("a start must be followed by its snapshot");
                        Protocol.CheckSnapshot(start, snapshot);
                        try
                        {
                            Begin(start, snapshot, received.Elapsed.TotalSeconds);
                        }
                        catch (Exception e) when (e is not (OutOfMemoryException or ProtocolException))
                        {
                            NotStarted(start, e);
                        }
                        break;
                    case MessageKind.SearchStart:
                        SearchStage(frame);
                        break;
                    case MessageKind.SearchPlan:
                        SearchPlan(frame);
                        break;
                    case MessageKind.SearchEnd:
                        DropSearch(Protocol.Decode<SearchEndMessage>(frame).SearchId);
                        break;
                    case MessageKind.Cancel:
                        var cancel = Protocol.Decode<CancelMessage>(frame);
                        if (_gate.Active == cancel.Generation)
                        {
                            _cancel?.Cancel();
                            if (IsSearchStage(cancel.Generation))
                                DropSearch();
                            EndPeak(cancel.Generation);
                            _gate.Cancel(cancel.Generation);
                            Log.Info($"run {cancel.Generation} cancelled");
                            GiveBackMemory();
                            _stock.RunEnded();
                        }
                        break;
                    case MessageKind.Warm:
                        var warm = Protocol.Decode<WarmMessage>(frame);
                        Log.Info($"warm: {warm.StockTarget} bare copies wanted");
                        _stock.Warm(warm.StockTarget, warm.ReserveBytes);
                        break;
                    case MessageKind.Release:
                        var dropped = _stock.Release();
                        Log.Info($"warm: released {dropped} bare copies");
                        if (_gate.Active == null)
                            GiveBackMemory();
                        break;
                    case MessageKind.Dev:
                        var handled = false;
                        DevFrame(frame, ref handled);
                        if (!handled)
                            _devFallback.NotHandled(Log.Warn);
                        break;
                    case MessageKind.Stop:
                        Log.Info("stop asked");
                        return;
                    default:
                        throw new ProtocolException($"unexpected {frame.Kind} from the game");
                }
            }
            Log.Info("the game closed the pipe");
        }
        catch (ProtocolException e)
        {
            Log.Error($"protocol: {e.Message}");
        }
        finally
        {
            _stopping = true;
            _cancel?.Cancel();
        }
    }

    private void Begin(StartMessage start, byte[] bytes, double transferSeconds)
    {
        // The pool's threads start in this scope, so every line of the run carries its id.
        using var scope = LogScope.Push(run: start.RunId, side: start.Side is [var side] ? side : null);
        _gate.Start(start.Generation);
        // A run takes the host: a search's kept copies go.
        DropSearch();
        _peak.Start(ProcessMeter.PrivateBytes());
        GuardLog.Reset();
        var snapshot = new Snapshot(start.Character, bytes, start.Sha256, DateTime.UtcNow, start.Build, 0);
        var job = new PoolJob(
            snapshot,
            start.LoadoutSlot,
            [
                .. start.Targets.Select(key =>
                    TargetSpec.FromKey(key) with
                    {
                        SingleTask = start.SingleTasks?.GetValueOrDefault(key),
                    }
                ),
            ],
            new DriveOptions(
                start.Options.Trials,
                start.Options.Limits,
                start.Options.IgnoreRequirements,
                start.Options.StopPrecision
            ),
            start.Seed,
            start.Copies,
            start.Reuse,
            start.TargetSeeds,
            Changes: start.Changes is { } changes ? ChangeList.Parse(changes) : null,
            RunId: start.RunId,
            Side: start.Side is [var s] ? s : null,
            PartLoadouts: start.PartLoadouts
        );
        _cancel?.Dispose();
        _cancel = new CancellationTokenSource();
        var report = new Report(this, start.Generation, start.Copies, transferSeconds);
        _pool = report.Pool = CopyPool.Start(
            job,
            report,
            _cancel.Token,
            MemoryReserve.For(start.ReserveBytes),
            _stock.Take,
            _stock.Count
        );
        Log.Info(
            $"run {start.Generation}: {start.Targets.Count} targets, seed {start.Seed}, copies {start.Copies}, reserve {start.ReserveBytes / (1024 * 1024)} MB"
        );
        // The next run is already known (a comparison's second side): build its copies while this one fights.
        if (start.NextRunCopies > 0)
            _stock.Warm(start.NextRunCopies, start.ReserveBytes, duringRun: true);
    }

    // A run the host can't start (a target key or change it doesn't know) fails alone; the host serves on.
    private void NotStarted(StartMessage start, Exception error)
    {
        using var scope = LogScope.Push(run: start.RunId, side: start.Side is [var side] ? side : null);
        Log.Error($"run {start.Generation} not started", error);
        Send(MessageKind.Failed, new FailedMessage(start.Generation, $"{error.GetType().Name}: {error.Message}"));
        EndPeak(start.Generation);
        _gate.End(start.Generation);
    }

    // The run's own peak as it ends; 0 for a run that isn't the active one any more (a cancelled run's late end).
    private long EndPeak(int generation) => _gate.Active == generation ? _peak.End(ProcessMeter.PrivateBytes()) : 0;

    // What the collector runs under, so a memory figure in this log names its settings.
    private static string GcSettingsLine()
    {
        var config = GC.GetConfigurationVariables();
        string Of(string name) => config.TryGetValue(name, out var value) ? $"{value}" : "?";
        var set = System
            .Environment.GetEnvironmentVariables()
            .Keys.Cast<string>()
            .Where(k => k.StartsWith("DOTNET_GC", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(k => $"{k}={System.Environment.GetEnvironmentVariable(k)}");
        return $"gc: {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}, processors {System.Environment.ProcessorCount}, heaps {Of("HeapCount")}, concurrent {Of("ConcurrentGC")}, set [{string.Join(" ", set)}], config [{string.Join(" ", config.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key}={c.Value}"))}]";
    }

    // Between runs the host's copies are garbage: collect once, aggressively, so its memory goes back to the
    // machine rather than sitting idle until the next run. Late builds of a cancelled run are caught by the next.
    private void GiveBackMemory() =>
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(500);
            if (_gate.Active != null)
                return;
            CollectEverything();
        });

    private static void CollectEverything()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System
            .Runtime
            .GCLargeObjectHeapCompactionMode
            .CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    /// <summary>How often a run's memory is read for its peak: a fight's allocation spikes last longer than this.</summary>
    private static readonly TimeSpan PeakInterval = TimeSpan.FromMilliseconds(250);

    private const int TicksPerBeat = 4;

    private void Beat()
    {
        var meter = new ProcessMeter();
        for (var tick = 1; !_stopping; tick++)
        {
            Thread.Sleep(PeakInterval);
            var hung = false;
            Hung(ref hung);
            if (hung || _stopping)
                continue;
            var beatNow = tick % TicksPerBeat == 0;
            BeforeBeat(ref beatNow);
            if (!beatNow)
            {
                if (_gate.Active != null)
                    _peak.Read(ProcessMeter.PrivateBytes());
                continue;
            }
            var (cpu, privateBytes, peak) = meter.Read();
            _peak.Read(privateBytes);
            var (heap, committed) = ProcessMeter.Heap();
            var active = _gate.Active ?? 0;
            Send(
                MessageKind.Heartbeat,
                new HeartbeatMessage(
                    privateBytes,
                    peak,
                    MemoryReserve.Available(),
                    active != 0 ? _pool?.Fighting ?? 0 : 0,
                    active,
                    cpu,
                    _stock.Count,
                    _stock.Target,
                    _stock.State,
                    heap,
                    committed,
                    _peak.Bytes
                )
            );
        }
    }

    private void Send<T>(MessageKind kind, T message)
    {
        var hung = false;
        Hung(ref hung);
        if (hung)
            return;
        try
        {
            _writer.Send(kind, message);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            if (!_stopping)
                Log.Warn("the game's pipe is gone, stopping", e);
            _stopping = true; // Core ends this process
        }
    }

    // Pool output → frames, each tagged with the run it belongs to.
    private sealed class Report(HostServer server, int generation, int copies, double transferSeconds) : IPoolReport
    {
        public CopyPool? Pool { get; set; }

        public void State(string key, TargetState state) =>
            server.Send(MessageKind.State, new StateMessage(generation, key, state));

        public void MainSkill(string? skill) =>
            server.Send(MessageKind.MainSkill, new MainSkillMessage(generation, skill));

        public void Progress(string key, int trialsDone) =>
            server.Send(MessageKind.Progress, new ProgressMessage(generation, key, trialsDone));

        public void Done(TargetResult result) => server.Send(MessageKind.Done, new DoneMessage(generation, result));

        public void CopyDropped(string error) =>
            server.Send(MessageKind.CopyDropped, new CopyDroppedMessage(generation, error));

        public void Finished(PoolStats stats)
        {
            SendGuards();
            server.Send(
                MessageKind.Finished,
                new FinishedMessage(
                    generation,
                    stats with
                    {
                        TransferSeconds = transferSeconds,
                    },
                    server.EndPeak(generation),
                    Pool?.MinCopies ?? copies
                )
            );
            server._gate.End(generation);
            server.GiveBackMemory();
            server._stock.RunEnded();
        }

        public void Failed(Exception error)
        {
            Log.Error($"run {generation} failed", error);
            SendGuards();
            var text = error is NotEnoughMemoryException ? error.Message : $"{error.GetType().Name}: {error.Message}";
            server.Send(MessageKind.Failed, new FailedMessage(generation, text));
            server.EndPeak(generation);
            server._gate.End(generation);
            server.GiveBackMemory();
            server._stock.RunEnded();
        }

        private void SendGuards()
        {
            var (skipped, blocked) = GuardLog.Export();
            server.Send(MessageKind.Guards, new GuardsMessage(generation, skipped, blocked));
        }
    }

    // Dev builds only: another CS2 build in the hello, to check the mismatch refusal.
    static partial void HelloCs2(ref string cs2);

    // Dev builds only: a dev message (hang, crash); sets handled when it acted on it.
    partial void DevFrame(Frame frame, ref bool handled);

    // Dev builds only: the host was told to hang, so it sends nothing more.
    partial void Hung(ref bool hung);

    // Dev builds only: on the heartbeat thread before each reading; a collection asked for runs here and sets
    // beatNow, so the next heartbeat is the reading right after it.
    partial void BeforeBeat(ref bool beatNow);
}
