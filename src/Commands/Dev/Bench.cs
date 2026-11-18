using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Dev;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// Measures how the live game runs in a control window with CS2 idle, then in a run window while the real pool fights
/// a target over and over on N copies (fresh copies and building ahead included), in the host or the game's process.
/// With the host, it is started and idle before the control window, so its launch isn't counted against the run;
/// <c>cold</c> starts it inside the run window instead, for the record. Sampled every frame on the main thread.
/// Tick lag is wall time × ticks per second − physics frames since the window started.
/// </summary>
internal static class Bench
{
    private sealed class NoReport : IPoolReport
    {
        public void State(string key, TargetState state) { }

        public void MainSkill(string? skill) { }

        public void Progress(string key, int trialsDone) { }

        public void Done(TargetResult result) { }

        public void CopyDropped(string error) { }

        public void Finished(PoolStats stats) { }

        public void Failed(Exception error)
        {
            Error = error.Message;
            Log.Error($"bench pool failed: {error}");
        }

        public string? Error { get; private set; }
    }

    private static FrameWindow? _window;
    private static string _phase = "idle";
    private static double _seconds;
    private static WindowStats? _control;
    private static WindowStats? _run;
    private static CancellationTokenSource? _cancel;
    private static NoReport? _report;
    private static Action? _startPool;
    private static string _where = RunService.InGame;
    private static int _copies;
    private static long _hostPeak;
    private static int _minCopies = int.MaxValue;
    private static int _next;
    private static System.Runtime.GCLatencyMode? _restore;

    public static string Id { get; private set; } = "none";

    /// <summary>The last bench's control window, once it has one.</summary>
    public static WindowStats? Control => _control;

    /// <summary>Whether the last bench's run window kept within every limit; null until it has finished.</summary>
    public static bool? Passed =>
        _control is { } c && _run is { } r ? BenchLimits.Pass(BenchLimits.Evaluate(c, r)) : null;

    public static bool Running => _phase is "warming" or "control" or "run" or "measure";

    /// <summary>Starts the bench: (host start), control window, run window of the same length. Main thread.</summary>
    public static void Start(
        TargetSpec target,
        int copies,
        double seconds,
        string where,
        bool cold,
        bool lowLatency = false,
        int trials = 1000
    )
    {
        MainThread.Assert("Bench.Start");
        if (Running)
            throw new CommandException($"{Id} is still running");
        if (RunService.Instance.IsActive)
            throw new CommandException("a run is active: cancel it first");
        Id = $"bench-{++_next}";
        _seconds = seconds;
        _control = _run = null;
        _where = where;
        _copies = copies;
        _hostPeak = 0;
        _minCopies = int.MaxValue;
        var snapshot = LiveGame.TakeSnapshot(LiveGame.Require());
        var generation = 1_000_000 + _next;
        _startPool = () =>
        {
            _cancel = new CancellationTokenSource();
            _report = new NoReport();
            // The same target over and over; the pool stops taking new ones when the window ends.
            var targets = Enumerable.Repeat(target, 10_000).ToList();
            var job = new PoolJob(
                snapshot,
                null,
                targets,
                new DriveOptions(trials, new TrialLimits(), false),
                1,
                copies
            );
            if (lowLatency)
            {
                _restore = System.Runtime.GCSettings.LatencyMode;
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.LowLatency;
            }
            if (where == RunService.InHost)
                RemotePool.Start(job, _report, generation, _cancel.Token);
            else
                CopyPool.Start(job, _report, _cancel.Token);
        };
        Log.Info(
            $"{Id}: {seconds:0} s control, then {seconds:0} s of {target.Key} on {copies} copies in {where}{(cold ? " (cold host)" : "")}"
        );
        if (where != RunService.InHost)
        {
            BeginControl();
            return;
        }
        // The host is started (or, cold, stopped) off the main thread before the control window begins.
        _phase = "warming";
        var host = HostClient.Instance;
        Task.Run(() =>
        {
            string? error = null;
            try
            {
                if (cold)
                    host.Stop();
                else
                    host.Ensure();
            }
            catch (HostException e)
            {
                error = e.Message;
            }
            MainThread.Post(() =>
            {
                if (_phase != "warming")
                    return;
                if (error != null)
                {
                    _phase = "failed: " + error;
                    return;
                }
                BeginControl();
            });
        });
    }

    /// <summary>
    /// Samples the live game's frames for a while, whatever is going on (a run, the window open). Main thread.
    /// </summary>
    public static void Measure(double seconds)
    {
        MainThread.Assert("Bench.Measure");
        if (Running)
            throw new CommandException($"{Id} is still running");
        Id = $"fps-{++_next}";
        _seconds = seconds;
        _control = _run = null;
        _phase = "measure";
        _window = new FrameWindow();
    }

    private static void BeginControl()
    {
        _phase = "control";
        _window = new FrameWindow();
    }

    /// <summary>Frame callback: samples the current window and ends it when its time is up.</summary>
    public static void Sample()
    {
        var w = _window;
        if (w == null)
            return;
        w.Sample();
        if (_phase == "run" && _where == RunService.InHost && HostClient.Instance.LastBeat is { } beat)
        {
            _hostPeak = Math.Max(_hostPeak, beat.RunPeakPrivateBytes);
            if (beat.Copies > 0 && w.Watch.Elapsed.TotalSeconds > 5) // after the first builds
                _minCopies = Math.Min(_minCopies, beat.Copies);
        }
        if (w.Watch.Elapsed.TotalSeconds < _seconds)
            return;
        if (_phase == "measure")
        {
            var m = w.Stats();
            _window = null;
            _phase = string.Create(
                CultureInfo.InvariantCulture,
                $"done: fps {m.Fps:0.0}, frame p50 {m.FrameP50:0.0} p99 {m.FrameP99:0.0} max {m.FrameMax:0.0} ms, {m.Frames} frames, {m.Collections}"
            );
            return;
        }
        if (_phase == "control")
        {
            _control = w.Stats();
            _phase = "run";
            _startPool?.Invoke();
            _startPool = null;
            _window = new FrameWindow();
            return;
        }
        _run = w.Stats();
        _window = null;
        RestoreLatency();
        _phase = "done";
        _cancel?.Cancel();
        Log.Info(Result());
    }

    private static void RestoreLatency()
    {
        if (_restore is { } mode)
            System.Runtime.GCSettings.LatencyMode = mode;
        _restore = null;
    }

    public static void Stop()
    {
        RestoreLatency();
        _cancel?.Cancel();
        _window = null;
        _startPool = null;
        if (Running)
            _phase = "stopped";
    }

    public static string Result()
    {
        if (_control is not { } c || _run is not { } r)
            return $"{Id} {_phase}";
        var limits = BenchLimits.Evaluate(c, r);
        string V(string name) => Fmt(limits.Single(l => l.Name == name).Value);
        string C(string name) => Fmt(limits.Single(l => l.Name == name).Control);
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Id} {(BenchLimits.Pass(limits) ? "pass" : "fail")} in {_where} fps {V("fps")} (control {C("fps")}) tick-lag max {V("tick-lag")} (control {C("tick-lag")}) lost {V("lost")} (control {C("lost")}) long {V("long")} (control {C("long")}) gc-pause {r.GcPauseMs:0} (control {c.GcPauseMs:0})"
            )
        );
        if (_where == RunService.InHost)
            text.Append(
                CultureInfo.InvariantCulture,
                $" host-peak {_hostPeak / 1048576} copies {(_minCopies == int.MaxValue ? 0 : _minCopies)}/{_copies}"
            );
        if (_report?.Error is { } error)
            text.Append("\npool failed: ").Append(error);
        foreach (var l in limits)
            text.Append(
                CultureInfo.InvariantCulture,
                $"\n{l.Name} {Fmt(l.Value)} {Fmt(l.Control)} {Fmt(l.Bound)} {(l.Pass ? "pass" : "fail")}"
            );
        text.Append(
            CultureInfo.InvariantCulture,
            $"""

            control: {c.Collections}, frame p50 {c.FrameP50:0.0} p99 {c.FrameP99:0.0} max {c.FrameMax:0.0} ms, {c.Frames} frames
            run:     {r.Collections}, frame p50 {r.FrameP50:0.0} p99 {r.FrameP99:0.0} max {r.FrameMax:0.0} ms, {r.Frames} frames
            gc: server {System.Runtime.GCSettings.IsServerGC}, latency {System.Runtime.GCSettings.LatencyMode}
            """
        );
        return text.ToString();
    }

    private static string Fmt(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
