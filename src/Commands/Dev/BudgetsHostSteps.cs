using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Dev;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Panel;
using static MelvorMods.CombatSimulator2.Commands.Dev.BudgetsBench;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>The budgets bench's steps on the simulator host: what it costs the game's frames, and its memory.</summary>
internal static class BudgetsHostSteps
{
    private const double SmoothSeconds = 20;

    // The run bench, then two more windows judged by the same limits against its control: the host starting and
    // filling its stock, and the host ready with nothing to do.
    public static IEnumerable<object?> Smoothness()
    {
        Step("smoothness: a run");
        Cs2Modal.Close();
        var target = Targets.Resolve(LiveGame.Require(), "GolbinForest/Golbin");
        Bench.Start(target, Engine.DefaultCopies.Now, SmoothSeconds, RunService.InHost, cold: false);
        foreach (var _ in Until(() => !Bench.Running, 3 * SmoothSeconds + 60))
            yield return null;
        if (Bench.Control is not { } control || Bench.Passed is not { } run)
        {
            Set(new NotMeasuredLine("smoothness", $"the run bench ended as: {Bench.Result()}"));
            yield break;
        }
        var host = HostClient.Instance;
        Step("smoothness: the host warming up");
        host.Stop();
        foreach (var _ in Wait(1))
            yield return null;
        WindowStats? warming = null;
        Cs2Modal.Open(Cs2Modal.Simulate);
        foreach (var _ in Window(SmoothSeconds, null, s => warming = s))
            yield return null;
        Step("smoothness: the host idle");
        foreach (var _ in Until(() => host.LastBeat is { Warm: WarmState.Ready }, 60))
            yield return null;
        WindowStats? idle = null;
        foreach (var _ in Window(SmoothSeconds, null, s => idle = s))
            yield return null;
        // The two windows above have the panel open, the control has it closed. The same idle host with the
        // panel closed tells the host's cost from the open panel's own frames.
        Step("smoothness: the host idle, panel closed");
        Cs2Modal.Close();
        foreach (var _ in Wait(1))
            yield return null;
        WindowStats? closed = null;
        foreach (var _ in Window(SmoothSeconds, null, s => closed = s))
            yield return null;
        Set(
            new LimitsLine(
                "smoothness",
                [
                    ("run", run),
                    ("warm-up", Judge("warm-up", control, warming!)),
                    ("idle", Judge("idle", control, idle!)),
                    ("idle-closed", Judge("idle, panel closed", control, closed!)),
                ]
            )
        );
    }

    // Whether the window kept every limit; the ones it missed go to the log with both values.
    private static bool Judge(string name, WindowStats control, WindowStats window)
    {
        var results = BenchLimits.Evaluate(control, window);
        var missed = results
            .Where(r => !r.Pass)
            .Select(r => $"{r.Name} {r.Value:0.#} (control {r.Control:0.#}, bound {r.Bound:0.#})");
        Common.Log.Info(
            $"smoothness {name}: {(BenchLimits.Pass(results) ? "pass" : "missed " + string.Join(", ", missed))}; frame p50 {window.FrameP50:0.0} p99 {window.FrameP99:0.0} max {window.FrameMax:0.0} ms, {window.Collections}"
        );
        return BenchLimits.Pass(results);
    }

    /// <summary>What the smallest supported machine has free when a run starts, after Windows, a browser and the game.</summary>
    private const long LowMemoryRoom = 1L << 30;

    // Each take on a fresh host: the stock, a run at the default copies, a run on another fresh host with the
    // reserve raised until about 1 GB is left, then the panel closed: the stock goes and then the host, on
    // delays shortened for the bench.
    private static readonly TimeSpan QuickRelease = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QuickStop = TimeSpan.FromSeconds(15);

    private static bool Exited(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true; // no such process any more
        }
    }

    public static IEnumerable<object?> Memory()
    {
        var host = HostClient.Instance;
        var service = RunService.Instance;
        var reserve = host.ReserveBytes;
        // The stock's release and the idle stop are the same steps at any length: seconds here, not minutes.
        RunService.SetReleaseDelay(QuickRelease);
        host.Idle.ClosedLimit = QuickStop;
        var (stock, peak, low, idle, stopped) = (
            new List<double>(),
            new List<double>(),
            new List<double>(),
            new List<double>(),
            new List<double>()
        );
        var lowCopies = new List<int>();
        string? problem = null;
        for (var take = 0; take < Repeats && problem == null; take++)
        {
            Step($"memory: a fresh host and its stock, take {take + 1}");
            Cs2Modal.Close();
            host.Stop();
            foreach (var _ in Wait(1))
                yield return null;
            Cs2Modal.Open(Cs2Modal.Simulate);
            foreach (var _ in Until(() => host.LastBeat is { Warm: WarmState.Ready }, 120))
                yield return null;
            if (host.LastBeat is not { Warm: WarmState.Ready })
            {
                problem = "the host's stock wasn't ready within 2 minutes";
                break;
            }
            long most = 0;
            foreach (var _ in Until(() => Read(host, ref most) && false, 5))
                yield return null;
            stock.Add(Mb(most));

            Step($"memory: run all, take {take + 1}");
            StartRun();
            foreach (var _ in Until(() => !service.IsActive, 300))
                yield return null;
            peak.Add(Mb(RemotePool.LastFinished?.PeakPrivateBytes ?? 0));
            foreach (var _ in Wait(3))
                yield return null;

            // A machine short of memory is short before the host starts: a fresh host, with room for 1 GB in all.
            Step($"memory: run all with little memory free, take {take + 1}");
            Cs2Modal.Close();
            var old = host.Pid;
            host.Stop();
            foreach (var _ in Until(() => old is not { } pid || Exited(pid), 15))
                yield return null;
            // The room is set from the fresh host's own readings, a moment before the run decides on them: the
            // machine's available memory moves by more than a copy's worth between two of this game's readings.
            // Until then the reserve is everything, so no stock is built on memory the case doesn't have.
            host.SetReserveBytes(MemoryReserve.Available());
            Cs2Modal.Open(Cs2Modal.Simulate);
            foreach (var _ in Until(() => host.LastBeat != null, 60))
                yield return null;
            foreach (var _ in Wait(3))
                yield return null;
            if (host.LastBeat is not { } beat)
            {
                problem = "the fresh host for the low-memory run didn't start";
                break;
            }
            host.SetReserveBytes(Math.Max(reserve, beat.AvailableBytes - (LowMemoryRoom - beat.PrivateBytes)));
            StartRun();
            foreach (var _ in Until(() => !service.IsActive, 600))
                yield return null;
            host.SetReserveBytes(reserve);
            low.Add(Mb(RemotePool.LastFinished?.PeakPrivateBytes ?? 0));
            lowCopies.Add(RemotePool.LastFinished?.MinCopies ?? 0);

            Step($"memory: idle after the panel closes, take {take + 1}");
            Cs2Modal.Close();
            var closedAt = Environment.TickCount64;
            // Half-way between the stock's release and the host's stop.
            var idleAt = (QuickRelease + QuickStop).TotalSeconds / 2;
            foreach (var _ in Until(() => Environment.TickCount64 - closedAt > idleAt * 1000, idleAt + 5))
                yield return null;
            idle.Add(Mb(host.LastBeat?.PrivateBytes ?? 0));
            Step($"memory: the host stopped, take {take + 1}");
            var stopBy = QuickStop.TotalSeconds + 5;
            foreach (
                var _ in Until(() => Environment.TickCount64 - closedAt > stopBy * 1000 || host.Pid == null, stopBy + 5)
            )
                yield return null;
            stopped.Add(host.Pid == null ? 0 : 1);
        }
        foreach (
            var (name, takes, unit, note) in new (string, List<double>, string, string?)[]
            {
                ("host-stock", stock, "MB", null),
                ("host-peak", peak, "MB", null),
                ("host-peak-low", low, "MB", $"fewest copies fighting {string.Join(",", lowCopies)}"),
                ("host-idle", idle, "MB", null),
                ("host-stopped", stopped, "hosts", null),
            }
        )
        {
            if (!Wants(name))
                continue;
            var limit = Budget(name).Limit;
            Set(
                takes.Count == 0
                    ? new NotMeasuredLine(name, problem ?? "no take finished")
                    : new ValueLine(name, takes, name == "host-stopped" ? limit : limit / 1048576.0, unit, note)
            );
        }
    }

    private static bool Read(HostClient host, ref long most)
    {
        most = Math.Max(most, host.LastBeat?.PrivateBytes ?? 0);
        return true;
    }

    private static double Mb(long bytes) => bytes / 1048576;
}
