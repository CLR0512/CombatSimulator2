using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Host;

/// <summary>What must match between the live game and the host: game build, game assembly and CS2 assembly.</summary>
internal sealed record HostIdentity(string Build, string GameMvid, string Cs2Mvid);

internal enum HelloVerdict
{
    Ok,

    /// <summary>The first mismatch: stop this host and start one more.</summary>
    Restart,

    /// <summary>A second mismatch: runs are refused with the reason.</summary>
    Failed,
}

/// <summary>
/// The live side's view of the simulator host: its state, why it failed, and when it last answered. Godot-free,
/// driven by a clock so its timeouts can be tested. <paramref name="crashDumpBytes"/> gives the size of a crash dump
/// for the host's pid written since it started, or null: the runtime writes one as the host dies, and a host still
/// alive once its dump is complete is held by a system error box, so it is called crashed and the game ends it.
/// </summary>
internal sealed class HostHealth(
    Func<DateTime> clock,
    HostIdentity live,
    Func<int, DateTime, long?>? crashDumpBytes = null
)
{
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A dump no bigger after this long is complete.</summary>
    public static readonly TimeSpan DumpSettled = TimeSpan.FromSeconds(2);

    /// <summary>The longest a dump being written holds off calling the host hung or crashed.</summary>
    public static readonly TimeSpan DumpGrace = TimeSpan.FromMinutes(2);

    private long? _dumpBytes;
    private DateTime _dumpChangedAt;
    private DateTime _dumpSeenAt;

    private readonly Lock _lock = new();
    private int _mismatches;

    public HostState State { get; private set; }

    /// <summary>Why the host failed, in plain words, when <see cref="State"/> is failed.</summary>
    public Note? Reason { get; private set; }

    public DateTime? StartedAt { get; private set; }
    public DateTime? LastHeartbeat { get; private set; }

    /// <summary>The host's handshake, once it has said hello.</summary>
    public Hello? Hello { get; private set; }

    public TimeSpan? HeartbeatAge => LastHeartbeat is { } beat ? clock() - beat : null;

    private DateTime _idleSince;

    /// <summary>Since when the host has been ready with no run (its start or its last run's end); null otherwise.</summary>
    public DateTime? IdleSince
    {
        get
        {
            lock (_lock)
                return State == HostState.Ready ? _idleSince : null;
        }
    }

    public static string Name(HostState state) =>
        state switch
        {
            HostState.NotStarted => "not-started",
            _ => state.ToString().ToLowerInvariant(),
        };

    /// <summary>Records a host process starting: for a run, or (<paramref name="restart"/>) after a mismatch.</summary>
    public void Starting(bool restart = false)
    {
        lock (_lock)
        {
            if (!restart)
                _mismatches = 0;
            State = HostState.Starting;
            Reason = null;
            Hello = null;
            StartedAt = clock();
            LastHeartbeat = null;
            _dumpBytes = null;
        }
    }

    public HelloVerdict OnHello(Hello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        lock (_lock)
        {
            Hello = hello;
            var mismatch = Mismatch(hello);
            if (mismatch == null)
            {
                State = HostState.Ready;
                LastHeartbeat = _idleSince = clock();
                return HelloVerdict.Ok;
            }
            if (++_mismatches < 2)
                return HelloVerdict.Restart;
            Fail(mismatch, FailureKind.HostStart);
            return HelloVerdict.Failed;
        }
    }

    private Note? Mismatch(Hello hello)
    {
        if (hello.Build != live.Build || hello.GameMvid != live.GameMvid)
            return Note.Of(
                "the simulator loaded a different game build ({0}, live {1}); restart the game",
                hello.Build,
                live.Build
            );
        if (hello.Cs2Mvid != live.Cs2Mvid || hello.Protocol != Protocol.Version)
            return Note.Of("the simulator loaded a different CS2 version; reload CS2");
        return null;
    }

    public void StartFailed(Note reason)
    {
        lock (_lock)
            Fail(reason, FailureKind.HostStart);
    }

    /// <summary>Records that the host's pipe broke or carried something unreadable.</summary>
    public void Broke(Note reason)
    {
        lock (_lock)
            Fail(reason, State == HostState.Starting ? FailureKind.HostStart : FailureKind.HostCrash);
    }

    public void RunStarted()
    {
        lock (_lock)
        {
            if (State == HostState.Ready)
                State = HostState.Busy;
        }
    }

    public void RunEnded()
    {
        lock (_lock)
        {
            if (State == HostState.Busy)
            {
                State = HostState.Ready;
                _idleSince = clock();
            }
        }
    }

    public void Heartbeat()
    {
        lock (_lock)
            LastHeartbeat = clock();
    }

    /// <summary>Records that the process ended or its pipe broke.</summary>
    public void Exited(int? code)
    {
        lock (_lock)
        {
            if (State is HostState.Starting or HostState.Ready or HostState.Busy)
                Fail(
                    code is { } c
                        ? Note.Of("the simulator closed unexpectedly (code {0})", c)
                        : Note.Of("the simulator closed unexpectedly"),
                    State == HostState.Starting ? FailureKind.HostStart : FailureKind.HostCrash,
                    code
                );
        }
    }

    /// <summary>Applies the timeouts; true once, when they have just made the host fail.</summary>
    public bool Check()
    {
        lock (_lock)
        {
            var now = clock();
            if (State == HostState.Starting && StartedAt is { } started && now - started > StartTimeout)
            {
                Fail(
                    Note.Of(
                        "the simulator did not start within 30 s: the machine may be busy; try again, or simulate in the game's process"
                    ),
                    FailureKind.HostStart
                );
                return true;
            }
            if (State is HostState.Ready or HostState.Busy && Dying(now) is { } dying)
            {
                if (!dying)
                    return false;
                Fail(Note.Of("the simulator closed unexpectedly"), FailureKind.HostCrash);
                return true;
            }
            if (
                State is HostState.Ready or HostState.Busy
                && LastHeartbeat is { } beat
                && now - beat > HeartbeatTimeout
            )
            {
                Fail(Note.Of("the simulator stopped responding"), FailureKind.HostHang);
                return true;
            }
            return false;
        }
    }

    // Null with no crash dump; false while one is still being written, which holds off the heartbeat's timeout; true
    // once it is complete, or has been written for too long.
    private bool? Dying(DateTime now)
    {
        if (
            Hello is not { } hello
            || StartedAt is not { } started
            || crashDumpBytes?.Invoke(hello.Pid, started) is not { } bytes
        )
            return null;
        if (_dumpBytes == null)
            _dumpSeenAt = now;
        if (bytes != _dumpBytes)
        {
            _dumpBytes = bytes;
            _dumpChangedAt = now;
        }
        return now - _dumpChangedAt >= DumpSettled || now - _dumpSeenAt >= DumpGrace;
    }

    /// <summary>
    /// Records the host stopped on purpose (or CS2 unloading): nothing is running and nothing failed.
    /// </summary>
    public void Stopped()
    {
        lock (_lock)
        {
            State = HostState.NotStarted;
            Reason = null;
            Hello = null;
            _mismatches = 0;
            LastHeartbeat = null;
        }
    }

    /// <summary>
    /// The host has just failed, and how: raised once per failure, under the health's lock, so a handler only takes
    /// note and does its work elsewhere. The exit code comes with a crash when the process gave one.
    /// </summary>
    public event Action<FailureKind, Note, int?>? Failed;

    /// <summary>How the host last failed; null until it has.</summary>
    public FailureKind? FailedAs { get; private set; }

    private void Fail(Note reason, FailureKind kind, int? code = null)
    {
        var first = State != HostState.Failed;
        State = HostState.Failed;
        Reason = reason;
        if (!first)
            return;
        FailedAs = kind;
        Failed?.Invoke(kind, reason, code);
    }
}
