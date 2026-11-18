using System.Security.Cryptography;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine.Dev;

/// <summary>How a rerun is asked: where (<c>host</c> or <c>game</c>; null for where the run ran), copies, anyway.</summary>
internal sealed record ReplayOptions(string? In, int? Copies, bool Anyway);

/// <summary>Sends a dev lever to the simulator host; false when no host is running to take it.</summary>
internal delegate bool LeverSender(string lever);

/// <summary>
/// A report's run fought again: its own snapshot and request, every target fresh on its recorded seed, in the host
/// or the game's process, with the dev lever that made it fail sent again. It goes around
/// <see cref="RunService.Start"/>: the run is the replay's own, so nothing reaches the run on screen, the saved
/// runs, the reuse index or the comparisons, and the live game is read for its build alone. A comparison's sides
/// are fought one after the other, each matched on its own. Main thread.
/// </summary>
internal sealed class Replay : IDisposable
{
    // A lost host's record comes once it has exited and been dumped; a hung one is dumped for up to a minute.
    private static readonly TimeSpan HostRecordWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan HostStartWait = TimeSpan.FromSeconds(30);

    private readonly ReportFolder _report;
    private readonly ReplayOptions _options;
    private readonly LeverSender _send;
    private readonly Queue<Side> _sides;
    private readonly List<ReplayOutcome> _outcomes = [];
    private readonly List<string> _notes = [];
    private CancellationTokenSource? _cancel;

    private sealed record Side(RunRecord Record, char? Letter, Snapshot Snapshot, string SnapshotNote);

    private Replay(ReportFolder report, ReplayOptions options, LeverSender send, IEnumerable<Side> sides)
    {
        _report = report;
        _options = options;
        _send = send;
        _sides = new Queue<Side>(sides);
    }

    /// <summary>The replay under way or last ended this session.</summary>
    public static Replay? Last { get; private set; }

    public static bool IsActive => Last is { Done: false };

    private static int _order;

    /// <summary>When the last replay of a run started, in the order replays of runs and searches share.</summary>
    public static int LastStarted { get; private set; }

    /// <summary>The next place in that order.</summary>
    public static int Order() => ++_order;

    public bool Done { get; private set; }

    /// <summary>The run being fought now, for the reply while it lasts.</summary>
    public Run? Current { get; private set; }

    /// <summary>One line per side, then the notes; complete once <see cref="Done"/>.</summary>
    public IReadOnlyList<string> Lines() =>
        [
            .. _outcomes.Select(o => o.Line()),
            .. _outcomes.SelectMany(o => o.Notes).Concat(_notes).Distinct().Select(n => "note: " + n),
        ];

    /// <summary>Checks everything a rerun needs, then starts it; throws with the refusal, before anything runs.</summary>
    public static Replay Start(ReportFolder report, RunRecord run, ReplayOptions options, LeverSender send)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(run);
        MainThread.Assert("Replay.Start");
        Busy();
        if (report.GameBuild != LiveGame.GameBuild)
            throw new CommandException(
                $"the report is from game build {report.GameBuild}, this is {LiveGame.GameBuild}"
            );
        var records =
            run.Kind.StartsWith("comparison", StringComparison.Ordinal) && report.Pair(run) is { } pair
                ? (run.Kind.EndsWith('A') ? [run, pair] : new[] { pair, run })
                : [run];
        var sides = records.Select(r => Prepare(report, r, records.Length > 1, options.Anyway)).ToList();
        var replay = new Replay(report, options, send, sides);
        if (report.Cs2Mvid != null && report.Cs2Mvid != RunService.Cs2Mvid)
            replay._notes.Add(
                $"CS2 build differs from the report's ({RunService.Cs2Mvid} here, {report.Cs2Mvid} there)"
            );
        Last?.Dispose();
        Last = replay;
        LastStarted = Order();
        replay.Next();
        return replay;
    }

    private static void Busy()
    {
        if (IsActive)
            throw new CommandException($"{Last!.Current?.Id ?? "a replay"} is still running: cancel it first");
        if (RunService.Instance.IsActive)
            throw new CommandException(
                $"{RunService.Instance.Current?.Id ?? "a search"} is still running: cancel it first"
            );
    }

    private static Side Prepare(ReportFolder report, RunRecord record, bool comparison, bool anyway)
    {
        if (record.Missing() is [_, ..])
            throw new CommandException($"run {record.Id} has no request in the report");
        var failed = record.State == nameof(RunState.Error) || report.FailuresOf(record.Id).Count > 0;
        var (bytes, note) = report.SnapshotFor(
            record.Id,
            record.Snapshot,
            record.SnapshotSha256,
            record.SetupHash,
            failed,
            anyway
        );
        var snapshot = new Snapshot(
            record.Setup.Character,
            bytes,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            DateTime.UtcNow,
            record.GameBuild,
            0
        );
        return new Side(record, comparison ? record.Kind[^1] : null, snapshot, note);
    }

    public void Dispose() => _cancel?.Dispose();

    /// <summary>Cancels the side being fought; the replay ends with what it had.</summary>
    public void Cancel()
    {
        _sides.Clear();
        _cancel?.Cancel();
        _notes.Add("cancelled");
        Done = true;
        Current = null;
    }

    private void Next()
    {
        if (!_sides.TryDequeue(out var side))
        {
            Done = true;
            Current = null;
            Log.Info("replay ended: " + string.Join("; ", _outcomes.Select(o => o.Line())));
            return;
        }
        var where = _options.In ?? side.Record.Where ?? RunService.InHost;
        var levers = Levers(side, where);
        if (
            where == RunService.InHost
            && levers.Before.Count > 0
            && RunService.Host?.State is not (HostState.Ready or HostState.Busy)
        )
        {
            RunService.Remote?.Warm(Engine.DefaultCopies.Stock);
            MainThread.When(
                () => RunService.Host?.State is HostState.Ready or HostState.Busy,
                () => Fight(side, where, levers),
                HostStartWait,
                () => Fight(side, where, levers)
            );
            return;
        }
        Fight(side, where, levers);
    }

    private sealed record LeverPlan(List<string> Before, List<string> AtFirstFight, List<string> Notes);

    // A throw is sent again before the start, aimed at the target that failed, so the order targets are fought in
    // doesn't matter; the host's own failures when the first target starts fighting.
    private LeverPlan Levers(Side side, string where)
    {
        var plan = new LeverPlan([], [], []);
        foreach (var failure in _report.FailuresOf(side.Record.Id))
        {
            if (failure.Lever is not { } lever)
                continue;
            var aimed = lever == "throw" && failure.Facts.Target is { } target ? "throw " + target : lever;
            var early = aimed.StartsWith("throw", StringComparison.Ordinal);
            if (where == RunService.InGame && !early)
            {
                plan.Notes.Add($"lever {lever} not re-armed: it would stop the game's own process");
                continue;
            }
            (early ? plan.Before : plan.AtFirstFight).Add(aimed);
            plan.Notes.Add($"lever {aimed} re-armed");
        }
        return plan;
    }

    private void Fight(Side side, string where, LeverPlan levers)
    {
        var record = side.Record;
        var targets = record.Targets.Select(TargetSpec.FromKey).ToList();
        var request = new RunRequest(
            record.RunSetup(),
            targets,
            record.Trials,
            record.Seed,
            _options.Copies ?? record.Copies,
            where,
            Fresh: true,
            record.StopPrecision,
            record.Limits,
            record.TargetSeeds,
            ChangeOutcomes: record.ChangeOutcomes,
            Side: side.Letter
        );
        var id = "replay-" + record.Id + (side.Letter is { } letter ? "-" + letter : "");
        var run = RunPlan.NewRun(request, side.Snapshot, id, record.Seed ?? 0, where, t => t.Key);
        var slot = record.Setup.Loadout == Setup.Current ? null : record.Setup.Loadout;
        var job = RunPlan.Job(
            request,
            side.Snapshot,
            slot,
            targets,
            record.Seed ?? 0,
            run.Limits,
            RunService.ReuseCopies,
            id
        );
        var notes = new List<string>(levers.Notes) { $"rerun on {side.SnapshotNote}" };
        if (record.Where is { } ran && ran != where)
            notes.Add(
                ran == RunService.InGame
                    ? $"the run was made in the game's process; replayed in the {Name(where)}"
                    : $"the run was made in the host; replayed in the {Name(where)}"
            );
        Current = run;
        _cancel?.Dispose();
        _cancel = new CancellationTokenSource();
        var sink = new ReplaySink(
            run,
            () => SendAll(levers.AtFirstFight, where),
            (got, lost) => Ended(side, where, run, got, lost, notes)
        );
        foreach (var lever in levers.Before)
            Send(lever, where);
        Log.Info(
            $"replay {id} starts in {where}: {targets.Count} targets, seed {record.Seed}, copies {request.Copies}"
        );
        if (where == RunService.InHost)
            (RunService.Remote ?? throw new InvalidOperationException("no simulator host")).Start(
                job,
                sink,
                RunService.Instance.NextGeneration(),
                _cancel.Token
            );
        else
            CopyPool.Start(job, sink, _cancel.Token);
    }

    private static string Name(string where) => where == RunService.InGame ? "game's process" : "host";

    private void SendAll(List<string> levers, string where)
    {
        foreach (var lever in levers)
            Send(lever, where);
    }

    private void Send(string lever, string where)
    {
        if (where == RunService.InGame)
        {
            if (lever == "throw build")
                PoolFaults.ThrowNextBuild();
            else if (lever.StartsWith("throw ", StringComparison.Ordinal))
                PoolFaults.ThrowOn(lever["throw ".Length..]);
            return;
        }
        if (!_send(lever))
            Log.Warn($"replay: lever {lever} not sent, no host running");
    }

    // A lost host is recorded once it has exited, by the host's watcher, under the replay's run id.
    private void Ended(Side side, string where, Run run, List<FailureFacts> got, bool lost, List<string> notes)
    {
        if (!lost)
        {
            Match(side, where, run, got, notes);
            return;
        }
        var log = RunService.Instance.FailureLog;
        FailureFacts? host = null;
        void Seen(FailureRecord record)
        {
            if (
                record.RunId == run.Id
                && record.Kind is FailureKind.HostCrash or FailureKind.HostHang or FailureKind.HostStart
            )
                host = new FailureFacts(FailureRecord.Name(record.Kind), Phase(record.Phase), null, record.ExitCode);
        }
        foreach (var record in log.Newest())
            Seen(record);
        log.Recorded += Seen;
        MainThread.When(() => host != null, Finish, HostRecordWait, Finish);
        void Finish()
        {
            log.Recorded -= Seen;
            if (host != null)
                got.Insert(0, host);
            Match(side, where, run, got, notes);
        }
    }

    private static string? Phase(RunPhase? phase) => phase is { } p ? FailureRecord.Name(p) : null;

    private void Match(Side side, string where, Run run, List<FailureFacts> got, List<string> notes)
    {
        var record = side.Record;
        foreach (var result in run.Results.Values.Where(r => r.Reasons.Any(RunFailures.IsFailure)))
            got.Add(new FailureFacts(FailureRecord.Name(FailureKind.Target), "fight", result.Key));
        var expected = _report.FailuresOf(record.Id);
        if (expected.Count > 0)
        {
            foreach (var failure in expected)
                _outcomes.Add(ReplayMatch.Failure(record.Id, side.Letter, where, failure.Facts, got, notes));
        }
        else
        {
            var hash =
                run.State == RunState.Finished && run.Targets.All(run.Results.ContainsKey)
                    ? ResultsHashInput.Brief(run.Targets.Select(k => run.Results[k]))
                    : null;
            _outcomes.Add(
                ReplayMatch.Hash(
                    record.Id,
                    side.Letter,
                    where,
                    record.ResultHash ?? "none",
                    hash,
                    got.FirstOrDefault(),
                    notes
                )
            );
        }
        Next();
    }

    /// <summary>The replay's own report: results into its run, failures kept to match, the end handed back.</summary>
    private sealed class ReplaySink(Run run, Action firstFight, Action<List<FailureFacts>, bool> ended) : IPoolReport
    {
        private readonly List<FailureFacts> _got = [];
        private int _fighting;

        public void State(string key, TargetState state)
        {
            if (state == TargetState.Fighting && Interlocked.Exchange(ref _fighting, 1) == 0)
                MainThread.Post(firstFight);
            MainThread.Post(() =>
            {
                if (run.Results.GetValueOrDefault(key) is { IsFinal: false } result)
                    result.State = state;
            });
        }

        public void MainSkill(string? skill) => MainThread.Post(() => run.MainSkill = skill);

        public void Progress(string key, int trialsDone) { }

        public void Done(TargetResult result) => MainThread.Post(() => run.Results[result.Key] = result);

        public void CopyDropped(string error) =>
            MainThread.Post(() => _got.Add(new FailureFacts(FailureRecord.Name(FailureKind.Copy), "build")));

        public void Finished(PoolStats stats) =>
            MainThread.Post(() =>
            {
                run.State = RunState.Finished;
                run.EndedAt = DateTime.UtcNow;
                ended(_got, false);
            });

        public void Failed(Exception error) =>
            MainThread.Post(() =>
            {
                run.State = RunState.Error;
                run.EndedAt = DateTime.UtcNow;
                var lost = error is HostException { Lost: true };
                if (!lost)
                    _got.Add(new FailureFacts(FailureRecord.Name(FailureKind.Run)));
                ended(_got, lost);
            });
    }
}
