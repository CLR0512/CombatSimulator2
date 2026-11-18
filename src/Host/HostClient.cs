using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Host;

/// <summary>The current run's side of the host's output; called on the host reading thread.</summary>
internal interface IHostListener
{
    void OnFrame(Frame frame);

    /// <summary>The host failed or went away while this run was on it.</summary>
    void OnLost(Note reason);

    /// <summary>The run on the host and its targets fighting now, for a failure's record.</summary>
    (string? RunId, char? Side, string[]? InFlight) Running { get; }
}

/// <summary>
/// The live game's link to the simulator host: starts it on demand (off the main thread), checks its hello, reads
/// its frames on a thread of its own, watches its heartbeat, and replaces it after a failure on the next run.
/// </summary>
internal sealed partial class HostClient : IDisposable, IHostStatus
{
    public const string Role = "host";
    public const long DefaultReserveBytes = 1L << 30;

    private readonly Lock _startLock = new();
    private readonly Lock _lock = new();
    private readonly HostHealth _health;
    private readonly Timer _watch;
    private WorkerProcess? _process;
    private FrameWriter? _writer;
    private IHostListener? _listener;
    private bool _disposed;
    private bool _lostBadly; // the last host crashed or hung: its logs are kept as the crash's
    private int _sendFailed;

    private HostClient(HostIdentity live)
    {
        _health = new HostHealth(
            () => DateTime.UtcNow,
            live,
            (pid, since) => DataRoot is { } root ? HostDumps.CrashDumpBytes(HostDumps.Folder(root), pid, since) : null
        );
        _health.Failed += HealthFailed;
        _watch = new Timer(_ => Watch(), null, 1000, 1000);
    }

    private static HostClient? _instance;

    /// <summary>Set by the mod on load: starts a worker through Core.</summary>
    internal static Func<string, WorkerOptions, WorkerProcess>? Launcher { get; set; }

    /// <summary>
    /// The live game's identity, read on the main thread once the engine is up: the game build comes from Godot's
    /// project settings, which can't be read while mods load.
    /// </summary>
    public static HostIdentity? LiveIdentity { get; set; }

    /// <summary>The one host link; created on first use, after the game is up.</summary>
    public static HostClient Instance
    {
        get
        {
            lock (_create)
                return _instance ??= new HostClient(LiveIdentity ?? ThisProcess.Identity());
        }
    }

    private static readonly Lock _create = new();

    /// <summary>On unload: stops the host and forgets it.</summary>
    public static void Reset()
    {
        HostClient? old;
        lock (_create)
        {
            old = _instance;
            _instance = null;
        }
        old?.Dispose();
    }

    public HostState State => _health.State;
    public Note? Reason => _health.Reason;
    public DateTime? StartedAt => _health.StartedAt;
    public int? Pid => _process?.Pid;

    /// <summary>The last heartbeat: the host's memory, the machine's, and copies in use.</summary>
    public HeartbeatMessage? LastBeat
    {
        get => _beat?.Message;
        private set => _beat = value == null ? null : new LastReading(value, value.Reading());
    }

    // The reading made once per heartbeat, swapped as one reference with it: the panel reads it several times a second.
    private sealed record LastReading(HeartbeatMessage Message, HostReading Reading);

    private volatile LastReading? _beat;

    public HostReading? Reading => _beat?.Reading;

    /// <summary>Copies the current or last run asked for.</summary>
    public int AskedCopies { get; set; }

    /// <summary>Seconds from the start of the last host launch to its hello.</summary>
    public double? StartSeconds { get; private set; }

    public long ReserveBytes { get; private set; } = DefaultReserveBytes;

    /// <summary>
    /// When a ready host with no run is stopped, warm stock or not; the next run or panel opening starts it again
    /// (1–3 s). Warming doesn't count as a run.
    /// </summary>
    public HostIdle Idle { get; } = new(() => DateTime.UtcNow);

    /// <summary>The host's frame rate: its main loop only idles, the work runs on threads of its own.</summary>
    public const int MaxFps = 1;

    /// <summary>
    /// The host's runtime environment (GC settings), set by the mod; dev builds may add a fault lever's variable.
    /// </summary>
    public static Func<IReadOnlyDictionary<string, string>> Environment { get; set; } =
        () => new Dictionary<string, string>();

    /// <summary>CS2's own folder, set by the mod: the host's logs and dumps are under it.</summary>
    public static string? DataRoot { get; set; }

    /// <summary>
    /// Makes sure a host is ready: starts one if needed, checks its hello, and restarts once on a mismatch. Blocks for
    /// up to about 30 s per start: never call on the main thread. Throws <see cref="HostException"/> with the reason.
    /// </summary>
    public void Ensure()
    {
        lock (_startLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_health.State is HostState.Ready or HostState.Busy && _process is { HasExited: false })
                return;
            Drop();
            if (DataRoot != null)
            {
                HostLogFiles.Keep(Path.Combine(DataRoot, "logs"), _lostBadly, Log.Warn);
                HostDumps.Prune(HostDumps.Folder(DataRoot), keepBytes: RunService.CrashFilesBytes);
            }
            _lostBadly = false;
            _health.Starting();
            for (var restart = false; ; restart = true)
            {
                if (restart)
                    _health.Starting(restart: true);
                var process = Launch();
                var reader = new FrameReader(process.Channel);
                var hello = ReadHello(process, reader);
                switch (_health.OnHello(hello))
                {
                    case HelloVerdict.Ok:
                        StartSeconds = (DateTime.UtcNow - _health.StartedAt!.Value).TotalSeconds;
                        Attach(process, reader);
                        Log.Info($"host ready: pid {process.Pid} in {StartSeconds:0.0} s");
                        return;
                    case HelloVerdict.Restart:
                        Log.Warn("host hello mismatch: starting it once more");
                        process.Dispose();
                        continue;
                    default:
                        process.Dispose();
                        throw new HostException(_health.Reason!);
                }
            }
        }
    }

    private WorkerProcess Launch()
    {
        var launcher = Launcher ?? throw new HostException(Note.Of("the simulator can't start: CS2 is not loaded"));
        var environment = new Dictionary<string, string>(Environment());
        if (DataRoot != null)
            HostDumps.AddEnvironment(environment, DataRoot);
        LaunchEnvironment(environment);
        var options = new WorkerOptions(environment, MaxFps);
        LaunchOptions(ref options);
        try
        {
            return launcher(Role, options);
        }
        catch (WorkerStartException e)
        {
            _health.StartFailed(Note.Text(e.Message));
            throw new HostException(Note.Text(e.Message), e);
        }
    }

    // The first frame, within what is left of the 30 s start-up.
    private Hello ReadHello(WorkerProcess process, FrameReader reader)
    {
        var left = HostHealth.StartTimeout - (DateTime.UtcNow - _health.StartedAt!.Value);
        var read = Task.Run(reader.Read);
        try
        {
            if (!read.Wait(left > TimeSpan.Zero ? left : TimeSpan.Zero))
            {
                process.Kill();
                process.Dispose();
                _health.Check();
                throw new HostException(_health.Reason ?? Note.Of("the simulator did not start within 30 s"));
            }
            var frame = read.Result;
            if (frame is not { Kind: MessageKind.Hello } hello)
                throw new ProtocolException(
                    frame == null ? "it closed before saying hello" : $"it sent {frame.Value.Kind} first"
                );
            return Protocol.Decode<Hello>(hello);
        }
        catch (Exception e) when (e is AggregateException or ProtocolException)
        {
            var inner = e is AggregateException a ? a.InnerException ?? e : e;
            process.Kill();
            process.Dispose();
            var reason = process.HasExited
                ? Note.Of(
                    "the simulator closed while starting: an antivirus may be blocking a second Melvor2.exe; allow it, or simulate in the game's process"
                )
                : Note.Of(
                    "the simulator broke off while starting ({0}); try again, or simulate in the game's process",
                    inner.Message
                );
            _health.StartFailed(reason);
            throw new HostException(reason, inner);
        }
    }

    private void Attach(WorkerProcess process, FrameReader reader)
    {
        lock (_lock)
        {
            _process = process;
            _writer = new FrameWriter(process.Channel);
            _sendFailed = 0;
        }
        process.Exited += code => Lost(process, _health.State == HostState.Failed ? null : code, null);
        new Thread(() => ReadLoop(process, reader)) { IsBackground = true, Name = "cs2-host-reader" }.Start();
    }

    private void ReadLoop(WorkerProcess process, FrameReader reader)
    {
        Note? broke = null;
        try
        {
            while (reader.Read() is { } frame)
            {
                if (frame.Kind == MessageKind.Heartbeat)
                {
                    var beat = LastBeat = Protocol.Decode<HeartbeatMessage>(frame);
                    _health.Heartbeat();
                    Beat(beat);
                    continue;
                }
                _listener?.OnFrame(frame);
            }
        }
        catch (ProtocolException e)
        {
            broke = Note.Of("the simulator sent something unreadable ({0})", e.Message);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            Log.Warn($"host {process.Pid}: its pipe closed", e);
        }
        Lost(process, null, broke);
    }

    // The host process ended, its pipe broke, or it stopped answering: the run on it (if any) ends as an error.
    private void Lost(WorkerProcess process, int? code, Note? reason)
    {
        IHostListener? listener;
        lock (_lock)
        {
            if (process != _process)
                return;
            if (reason != null)
                _health.Broke(reason);
            else
                _health.Exited(code);
            listener = _listener;
            _listener = null;
            _process = null;
            _lostBadly = true;
            _writer = null;
            LastBeat = null;
        }
        HostCrash.BeforeKill(_health.FailedAs, process.Pid, DataRoot);
        process.Kill();
        process.Dispose();
        Log.Warn($"host lost: {_health.Reason?.English()}");
        listener?.OnLost(_health.Reason ?? Note.Of("the simulator closed unexpectedly"));
    }

    private void Watch()
    {
        if (_health.Check() && _process is { } process)
            Lost(process, null, _health.Reason);
        // Idle: stop it, unless a run is starting on it right now (it holds the start lock until it is busy).
        if (Idle.Passed(_health.IdleSince) != null && _startLock.TryEnter())
        {
            try
            {
                if (Idle.Passed(_health.IdleSince) is { } limit && Pid is { } pid)
                {
                    Log.Info($"host pid {pid} idle for {limit.TotalMinutes:0.#} min: stopping it");
                    Stop();
                }
            }
            finally
            {
                _startLock.Exit();
            }
        }
    }

    /// <summary>
    /// Runs <see cref="Ensure"/>, then hands the host to <paramref name="listener"/>'s run in the same step, so the
    /// idle stop can't come between them.
    /// </summary>
    public void EnsureFor(IHostListener listener)
    {
        lock (_startLock)
        {
            _starting = listener;
            try
            {
                Ensure();
            }
            finally
            {
                _starting = null;
            }
            BeginRun(listener);
        }
    }

    /// <summary>Gives <paramref name="listener"/> the host's frames until <see cref="EndRun"/>.</summary>
    private void BeginRun(IHostListener listener)
    {
        lock (_lock)
            _listener = listener;
        _health.RunStarted();
    }

    public void EndRun(IHostListener listener)
    {
        lock (_lock)
        {
            if (_listener != listener)
                return;
            _listener = null;
        }
        _health.RunEnded();
    }

    /// <summary>Sends a message; false if the host has gone (its loss is reported separately).</summary>
    public bool Send<T>(MessageKind kind, T message) => SendBytes(kind, Protocol.Encode(message));

    public bool SendBytes(MessageKind kind, byte[] payload)
    {
        var writer = _writer;
        if (writer == null)
            return false;
        try
        {
            writer.Write(kind, payload);
            return true;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Once per host (a write blocked on a hung host is cancelled when it is dropped): losing it says the rest.
            if (Interlocked.Exchange(ref _sendFailed, 1) == 0)
                Log.Warn($"host {_process?.Pid}: a message couldn't be sent", e);
            return false;
        }
    }

    /// <summary>
    /// Starts the host if needed and asks for <paramref name="stockTarget"/> bare copies. Blocks while the host starts
    /// (never call on the main thread); a run asked meanwhile waits on the same start and never starts a second host.
    /// </summary>
    public void Warm(int stockTarget)
    {
        Ensure();
        Send(MessageKind.Warm, new WarmMessage(stockTarget, ReserveBytes));
    }

    /// <summary>Drops the host's bare copies, if it is running.</summary>
    public void Release()
    {
        if (_process != null)
            Send(MessageKind.Release, new ReleaseMessage());
    }

    /// <summary>Stops the host cleanly; the next run starts a new one.</summary>
    public void Stop()
    {
        lock (_startLock)
        {
            Send(MessageKind.Stop, new StopMessage());
            Drop();
            _health.Stopped();
            Stopped();
        }
    }

    // Forgets the current process without reporting it as lost.
    private void Drop()
    {
        WorkerProcess? process;
        lock (_lock)
        {
            process = _process;
            _process = null;
            _writer = null;
            _listener = null;
            LastBeat = null;
        }
        process?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Stop();
        _disposed = true;
        _watch.Dispose();
    }

    // Dev builds only: the launch's environment and options, for the fault levers.
    partial void LaunchEnvironment(Dictionary<string, string> environment);

    partial void LaunchOptions(ref WorkerOptions options);

    // Dev builds only: a stop clears the levers that last until the host restarts.
    partial void Stopped();

    // Dev builds only: each heartbeat, for the reading after a forced collection.
    partial void Beat(HeartbeatMessage beat);
}
