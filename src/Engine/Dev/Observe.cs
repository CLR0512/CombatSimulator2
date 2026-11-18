using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine.Dev;

/// <summary>
/// Fidelity check: records live play against one enemy between start and stop (kills, XP per skill, consumables
/// used), then simulates the same target from the start snapshot and compares each rate with a z score.
/// Read only: it never touches the live fight. Main thread.
/// </summary>
internal static class Observe
{
    private sealed record Session(TargetSpec Target, Snapshot Snapshot, LivePoint Start);

    private static Session? _session;
    private static string? _report;

    public static string Start()
    {
        var game = LiveGame.Require();
        var current = LiveGame.Current(game) ?? throw new CommandException("not fighting: start a live fight first");
        var target = TargetSpec.FromKey(current.Key);
        if (target.Enemy == null)
            throw new CommandException("observe compares one enemy, not a dungeon");
        _session = new Session(target, LiveGame.TakeSnapshot(game), LiveGame.Observe(game, target));
        _report = null;
        return $"observing {Ids.BriefKey(target.Key)} from frame {_session.Start.Frame}";
    }

    /// <summary>Stops observing and starts the simulation; the comparison is ready when the run finishes.</summary>
    public static string Stop()
    {
        var session = _session ?? throw new CommandException("not observing (cs2dev observe start)");
        _session = null;
        var end = LiveGame.Observe(LiveGame.Require(), session.Target);
        var hours = (end.Frame - session.Start.Frame) / (double)LiveGame.TicksPerSecond / 3600;
        var run = RunService.Instance.Start(
            new RunRequest(new Setup(), [session.Target], 1000, 7, Engine.DefaultCopies.Now),
            session.Snapshot
        );
        MainThread.When(
            () => run.IsFinal,
            () => _report = Compare(run, session, end, hours),
            TimeSpan.FromSeconds(60),
            () => _report = "the simulation took longer than 60 s"
        );
        return string.Create(
            CultureInfo.InvariantCulture,
            $"observed {hours * 60:0.0} game min; simulating {run.Id} (cs2dev observe result)"
        );
    }

    public static string Result() => _report ?? (_session != null ? "observing" : "no result yet");

    // Observed rates use Poisson errors on counts; XP takes the kill rate's relative error (XP comes per hit).
    private static string Compare(Run run, Session session, LivePoint end, double hours)
    {
        var result = run.Results[session.Target.Key];
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Ids.BriefKey(session.Target.Key)}: {hours * 60:0.0} game min observed, {run.Id} simulated ({result.State.ToString().ToLowerInvariant()})"
            )
        );
        if (session.Start.Gear != end.Gear)
            text.Append("\nWARNING the live setup changed during the window");
        if (!result.HasEstimates || hours <= 0)
            return text.Append("\nno comparison: ").Append(result.Reasons.English(" | ")).ToString();
        var kills = end.Kills - session.Start.Kills;
        var killRate = kills / hours;
        var killSe = Math.Sqrt(Math.Max(kills, 1)) / hours;
        Row(text, "kills", killRate, killSe, Metrics.Estimate(result, Metrics.Kills, TimeUnit.Hour));
        foreach (var (skill, after) in end.Xp)
        {
            var gained = after - session.Start.Xp.GetValueOrDefault(skill);
            var sim = Metrics.Estimate(result, "xp:" + skill, TimeUnit.Hour);
            if (gained <= 0 && (sim == null || sim.Point == 0))
                continue;
            var rate = gained / hours;
            Row(text, "xp:" + Ids.Brief(skill), rate, killRate > 0 ? rate * killSe / killRate : double.NaN, sim);
        }
        foreach (var (key, before) in session.Start.Held)
        {
            var used = before - end.Held.GetValueOrDefault(key);
            var sim = Metrics.Estimate(result, "used:" + key, TimeUnit.Hour);
            // Only decreases are uses; increases are loot, which this check doesn't compare.
            if (used <= 0 && (sim == null || sim.Point == 0))
                continue;
            if (used < 0)
                used = 0;
            Row(text, "used:" + Ids.Brief(key), used / hours, Math.Sqrt(Math.Max(used, 1)) / hours, sim);
        }
        return text.ToString();
    }

    private static void Row(StringBuilder text, string name, double observed, double observedSe, Estimate? simulated)
    {
        if (simulated == null)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{name} observed {Fmt.N(observed)}/h, not in the simulation");
            return;
        }
        var z = Estimators.Z(observed, observedSe, simulated.Point, simulated.StandardError);
        text.Append(
            CultureInfo.InvariantCulture,
            $"\n{name} observed {Fmt.N(observed)} ± {Fmt.N(1.96 * observedSe)} /h, simulated {Fmt.Range(simulated)}, |z| {Math.Abs(z):0.00}{(Math.Abs(z) < 2 ? "" : "  ← over 2")}"
        );
    }
}
