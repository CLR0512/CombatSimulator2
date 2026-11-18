using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// A pool run in the simulator host, with the same shape as <see cref="CopyPool"/>: the same job in, the same report
/// calls out. Everything here runs off the main thread: starting the host, sending the snapshot, reading its output.
/// </summary>
internal sealed class RemotePool : IHostListener
{
    private readonly IPoolReport _report;
    private readonly int _generation;
    private readonly GenerationFilter _filter = new();
    private readonly HashSet<string> _fighting = new(StringComparer.Ordinal);
    private string? _runId;
    private char? _side;
    private int _ended;

    public (string? RunId, char? Side, string[]? InFlight) Running
    {
        get
        {
            lock (_fighting)
                return (_runId, _side, [.. _fighting]);
        }
    }

    private void Fighting(string key, bool fighting)
    {
        lock (_fighting)
        {
            if (fighting)
                _fighting.Add(key);
            else
                _fighting.Remove(key);
        }
    }

    private RemotePool(IPoolReport report, int generation)
    {
        _report = report;
        _generation = generation;
    }

    /// <summary>The last finished host run's peak memory and fewest copies, for the bench and status.</summary>
    public static FinishedMessage? LastFinished { get; private set; }

    /// <summary>Starts host runs for the engine.</summary>
    public static IRemotePool Starter { get; } = new Launcher();

    private sealed class Launcher : IRemotePool
    {
        public void Start(PoolJob job, IPoolReport report, int generation, CancellationToken token) =>
            RemotePool.Start(job, report, generation, token);

        public void Warm(int stockTarget) =>
            new Thread(() =>
            {
                try
                {
                    HostClient.Instance.Warm(stockTarget);
                }
                catch (HostException e)
                {
                    Log.Warn($"warm: {e.Reason.English()}");
                }
            })
            {
                IsBackground = true,
                Name = "cs2-warm-start",
            }.Start();

        public void Release() => HostClient.Instance.Release();

        public void Panel(bool open) => HostClient.Instance.Idle.Panel(open);

        public Engine.Search.ISearchFighter NewSearch() => new RemoteSearch();
    }

    public static void Start(PoolJob job, IPoolReport report, int generation, CancellationToken token)
    {
        var pool = new RemotePool(report, generation);
        new Thread(() => pool.Run(job, token)) { IsBackground = true, Name = "cs2-remote" }.Start();
    }

    private readonly System.Diagnostics.Stopwatch _wait = new();
    private double _hostWait;

    private void Run(PoolJob job, CancellationToken token)
    {
        _runId = job.RunId;
        _side = job.Side;
        _wait.Start();
        var client = HostClient.Instance;
        try
        {
            client.EnsureFor(this);
        }
        catch (HostException e)
        {
            End();
            _report.Failed(new HostException(e.Reason, e) { Lost = true });
            return;
        }
        _filter.Current = _generation;
        client.AskedCopies = job.Copies;
        if (token.IsCancellationRequested)
        {
            client.EndRun(this);
            return;
        }
        token.Register(() =>
        {
            if (End())
            {
                client.Send(MessageKind.Cancel, new CancelMessage(_generation));
                client.EndRun(this);
            }
        });
        var snapshot = job.Snapshot;
        var start = new StartMessage(
            _generation,
            snapshot.Character,
            snapshot.Sha256,
            snapshot.GameBuild,
            job.LoadoutSlot,
            [.. job.Targets.Select(t => t.Key)],
            new RunOptions(
                job.Options.Trials,
                job.Options.Limits,
                job.Options.IgnoreRequirements,
                job.Options.StopPrecision
            ),
            job.Seed,
            job.Copies,
            client.ReserveBytes,
            job.Reuse,
            job.TargetSeeds is { } seeds ? new Dictionary<string, ulong>(seeds, StringComparer.Ordinal) : null,
            job.NextRunCopies,
            job.Changes?.ShortForm,
            job.RunId,
            job.Side?.ToString(),
            job.PartLoadouts is { Count: > 0 } parts
                ? new Dictionary<string, string>(parts, StringComparer.Ordinal)
                : null,
            job.Targets.Any(t => t.SingleTask != null)
                ? job
                    .Targets.Where(t => t.SingleTask != null)
                    .ToDictionary(t => t.Key, t => t.SingleTask!, StringComparer.Ordinal)
                : null
        );
        _hostWait = _wait.Elapsed.TotalSeconds; // the host is up and takes the run next
        if (!client.Send(MessageKind.Start, start) || !client.SendBytes(MessageKind.Snapshot, snapshot.Bytes))
            OnLost(Note.Of("the simulator closed unexpectedly"));
    }

    // True the first time only: the run ends once, by finish, failure, loss or cancel.
    private bool End() => Interlocked.Exchange(ref _ended, 1) == 0;

    public void OnFrame(Frame frame)
    {
        switch (frame.Kind)
        {
            case MessageKind.State:
                var state = Protocol.Decode<StateMessage>(frame);
                if (_filter.Accept(state.Generation))
                {
                    Fighting(state.Key, state.State == TargetState.Fighting);
                    _report.State(state.Key, state.State);
                }
                break;
            case MessageKind.MainSkill:
                var skill = Protocol.Decode<MainSkillMessage>(frame);
                if (_filter.Accept(skill.Generation))
                    _report.MainSkill(skill.Skill);
                break;
            case MessageKind.Progress:
                var progress = Protocol.Decode<ProgressMessage>(frame);
                if (_filter.Accept(progress.Generation))
                    _report.Progress(progress.Key, progress.TrialsDone);
                break;
            case MessageKind.Done:
                var done = Protocol.Decode<DoneMessage>(frame);
                if (_filter.Accept(done.Generation))
                {
                    Fighting(done.Result.Key, false);
                    _report.Done(done.Result);
                }
                break;
            case MessageKind.CopyDropped:
                var dropped = Protocol.Decode<CopyDroppedMessage>(frame);
                if (_filter.Accept(dropped.Generation))
                    _report.CopyDropped(dropped.Error);
                break;
            case MessageKind.Guards:
                var guards = Protocol.Decode<GuardsMessage>(frame);
                if (_filter.Accept(guards.Generation))
                    GuardLog.Merge(guards.Skipped, guards.Blocked);
                break;
            case MessageKind.Finished:
                var finished = Protocol.Decode<FinishedMessage>(frame);
                if (!_filter.Accept(finished.Generation) || !End())
                    break;
                LastFinished = finished;
                Log.Info(
                    $"host run {finished.Generation}: peak {finished.PeakPrivateBytes / 1048576} MB, fewest copies fighting {finished.MinCopies}"
                );
                HostClient.Instance.EndRun(this);
                _report.Finished(finished.Stats with { HostWaitSeconds = _hostWait });
                break;
            case MessageKind.Failed:
                var failed = Protocol.Decode<FailedMessage>(frame);
                if (!_filter.Accept(failed.Generation) || !End())
                    break;
                HostClient.Instance.EndRun(this);
                _report.Failed(new HostException(Note.Of("the simulator failed: {0}", failed.Error)));
                break;
        }
    }

    public void OnLost(Note reason)
    {
        if (End())
            _report.Failed(new HostException(reason) { Lost = true });
    }
}
