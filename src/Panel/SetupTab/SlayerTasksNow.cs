using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// The Slayer page's "Your tasks now": each master's task in the save, its monster's rate on task against the master's
/// average, and whether to extend, keep or reroll it, with the gain over the kills involved and the coin it costs.
/// Each task is judged on whichever came last: Find best on its master, or a run of the master on side A; one action
/// runs the masters on side A again.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class SlayerTasksNow
{
    private readonly Label _sub;
    private readonly Button _run;
    private readonly VBoxContainer _rows;
    private string? _shownFor;

    // Side A as one setup line, written again only when its check changes.
    private static (SideCheck? Check, string? Line) _sideA;

    public SlayerTasksNow()
    {
        Root = Ui.Column(4, "Cs2SlayerTasksNow");
        var head = Ui.Add(Root, Ui.Row(12));
        var words = Ui.Add(head, Ui.Column(2));
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // Translators: Heading on the Slayer page: the save's current slayer tasks, each against its master's overall rate.
        words.AddChild(Ui.Label(Tr.Get("Your tasks now"), TextStyle.Heading));
        _sub = Ui.Label("", TextStyle.Small, "Cs2SlayerTasksSub", wrap: true);
        words.AddChild(_sub);
        // Translators: Button on the Slayer page: simulate every master the player has a task with.
        _run = Ui.Button(Tr.Get("Simulate tasks"), Look.Button4, "Cs2SlayerTasksRun", RunMasters);
        _run.TooltipText = Tr.Get(
            "Simulates each master you have a task with, on side A, to compare your task with the master's overall rate"
        );
        _run.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_run);
        _rows = Ui.Add(Root, Ui.Column(4, "Cs2SlayerTasksRows"));
    }

    public VBoxContainer Root { get; }

    /// <summary>
    /// Redrawn when the tasks, the run or the goal change; <paramref name="unit"/> names the goal's metric
    /// ("XP · Slayer") for the gains.
    /// </summary>
    public void Refresh(string metric, string unit)
    {
        if (LiveGame.Find() is not { } game)
            return;
        var tasks = SlayerTasks.Current(game);
        var service = RunService.Instance;
        var sources = tasks.ToDictionary(t => t.Master, t => Source(Ids.MasterKey(t.Master)), StringComparer.Ordinal);
        var key = string.Join(
            "|",
            metric,
            service.IsActive,
            PanelState.Look,
            string.Join(",", tasks.Select(t => $"{t.Master}:{t.Enemy}:{t.Progress}/{t.Length}:{t.Extended}")),
            string.Join(",", sources.Values.Select(s => s?.GetHashCode())),
            string.Join(",", Pending().Order(StringComparer.Ordinal))
        );
        if (key == _shownFor)
            return;
        _shownFor = key;
        Ui.Clear(_rows);
        Root.Visible = tasks.Count > 0;
        var names = PanelState.Names();
        var prices = LiveGame.Prices(game);
        // Best first, so the list also says which task to work on now; tasks not run yet last.
        var verdicts = tasks
            .Select(t =>
                (
                    Task: t,
                    Verdict: sources[t.Master] is { } r ? TaskVerdicts.For(t, r.Result, metric, prices) : null,
                    Best: sources[t.Master]?.Best == true,
                    Stale: sources[t.Master]?.Stale == true,
                    Blocked: sources[t.Master] is { Best: true } b
                        && (b.Result.Blocked ?? []).Any(k => k.EndsWith("/" + t.Enemy, StringComparison.Ordinal))
                )
            )
            .OrderByDescending(v => v.Verdict?.Ratio ?? double.MinValue)
            .ToList();
        var missing = verdicts.Count(v => v.Verdict == null);
        // Always there: a run ended after Find best is what the tasks are judged on, so simulating again catches up with
        // a changed setup.
        _run.Disabled = service.IsActive;
        _sub.Text =
            missing > 0
                ? Tr.Get("Simulate to compare each task with its master's overall rate.")
                : Tr.Get(
                    "Each task on its master's best setup or on side A, whichever was simulated last, against the master's overall rate: above it is worth extending, below it worth rerolling."
                );
        var pending = Pending();
        foreach (var (task, verdict, best, stale, blocked) in verdicts)
        {
            var row = Row(task, verdict, best, stale, blocked, unit, id => Art.Currency(game, id), names);
            if (pending.Contains(Ids.MasterKey(task.Master)))
                Widgets.Pending.Pulse(row, true);
            _rows.AddChild(row);
        }
    }

    /// <summary>
    /// The masters a run is working out again: those Find best hasn't done yet, and the masters of a run under way
    /// not finished yet.
    /// </summary>
    public static IReadOnlySet<string> Pending()
    {
        var service = RunService.Instance;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (service.BestMasters is { IsActive: true } best)
            keys.UnionWith(best.Masters.Skip(best.Done.Count));
        else if (service.BestLoadouts is { IsActive: true } one)
            keys.Add(one.Master);
        if (service.IsActive && service.Current is { } run)
            keys.UnionWith(
                run.Targets.Where(t => Ids.IsMaster(t) && run.Results.GetValueOrDefault(t) is not { IsFinal: true })
            );
        return keys;
    }

    // The master as its task is judged, whichever came last: its best setup once Find best has found one (each monster
    // on its picked loadout, the best blocks), as the task would be played, or the current run of it on side A, so
    // simulating the tasks again after a change is what they are judged on. Stale when side A has changed since Find
    // best started from it. Else Find best's fight with the setup.
    internal static (TargetResult Result, bool Best, bool Stale)? Source(string master)
    {
        var service = RunService.Instance;
        var job = service.BestLoadoutsFor(master);
        var current = service.Current;
        var run = current?.Results.GetValueOrDefault(master) is { State: TargetState.Finished, Parts.Count: > 0 } r
            ? r
            : null;
        // A run still going when its master finished ended after any Find best: only one runs at a time.
        var runEnded = run == null ? DateTime.MinValue : current!.EndedAt ?? DateTime.MaxValue;
        if (
            service.BestByMaster.FirstOrDefault(b => b.Master == master) is { Figure: not null } best
            && job is { State: BestLoadoutsState.Done, Picks: { } picks }
            && (job.EndedAt ?? DateTime.MinValue) >= runEnded
            && BestMasters.Picked(job.Results, picks, best.Blocked) is { } picked
        )
            return (picked, true, job.SideLine is { } from && SideALine() is { } now && now != from);
        if (run != null)
            return (run, false, false);
        return job?.Results.FirstOrDefault(r => r.Choice == null).Master is { } setup ? (setup, false, false) : null;
    }

    /// <summary>A side as its check found it, as one setup line; null without a check or when it can't be written.</summary>
    internal static string? LineOf(SideCheck? check) =>
        check != null
        && SetupLine.TryWrite(
            new LineContent(SetupLine.Version, LineKind.Setup, LineIds.Build, null, check.Line),
            out var text
        )
            ? text
            : null;

    /// <summary>Side A as one setup line, as <see cref="LineOf"/> writes it.</summary>
    internal static string? SideALine()
    {
        var check = PanelState.CheckA;
        if (!ReferenceEquals(check, _sideA.Check))
            _sideA = (check, LineOf(check));
        return _sideA.Line;
    }

    private static PanelContainer Row(
        TaskNow task,
        TaskVerdict? verdict,
        bool onBest,
        bool stale,
        bool blockedOnBest,
        string unit,
        Func<string, Texture2D?> coinOf,
        Func<string, string> names
    )
    {
        var box = Look.Panel(Art.RowBox, Ui.Id("Cs2SlayerTask", Ids.Brief(task.Master)));
        var line = Ui.Row(14);
        box.AddChild(Ui.Margin(line, 4, 0, 6, 0));
        var master = Ui.Label(names(task.Master), TextStyle.Small);
        master.CustomMinimumSize = new Vector2(80, 0);
        master.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(master);
        var monster = Ui.Clipped(names(task.Enemy), TextStyle.Body);
        monster.CustomMinimumSize = new Vector2(150, 0);
        monster.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        line.AddChild(monster);
        var progress = Ui.Label(Tr.Format("{0} of {1}", [task.Progress, task.Length]), TextStyle.Small);
        progress.CustomMinimumSize = new Vector2(80, 0);
        progress.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(progress);
        // The rate and the statement share the row and shorten with "…" (whole on hover): never wider than the page.
        var rate = Ui.Clipped(
            verdict == null
                ? Tr.Get("not simulated yet")
                : Tr.Format("{0} /h · {1}", [Ui.Number(verdict.Rate.Point), Against(verdict.Ratio, names(task.Master))])
                    // Translators: After a task's comparison when it was judged on side A, before Find best.
                    + (onBest ? "" : " · " + Tr.Get("on side A"))
                    // Translators: After a task's comparison on its master's best setup, when side A has changed since
                    // Find best found it.
                    + (stale ? " · " + Tr.Get("best found before your setup changed") : "")
                    // Translators: After a task's comparison when its master's best setup blocks the task's monster.
                    + (blockedOnBest ? " · " + Tr.Get("its best setup blocks it") : ""),
            TextStyle.Small
        );
        if (verdict != null)
        {
            // Translators: Hover on a task's comparison before Find best; {0} is the master.
            rate.TooltipText =
                rate.Text
                + "\n\n"
                + (
                    onBest
                        // Translators: Hover on a task's comparison once Find best has found its master's best; {0} the master.
                        ? Tr.Format(
                            "On {0}'s best loadouts and blocks. Overall rate: all of {0}'s tasks, blocked monsters left out.",
                            names(task.Master)
                        )
                        : Tr.Format("On side A. Find best on {0} judges it on its best loadouts.", names(task.Master))
                )
                + (
                    stale
                        // Translators: Hover on a task judged on a best setup found before side A changed.
                        ? "\n\n"
                            + Tr.Get(
                                "Your setup has changed since Find best. Find best again for the best setup now, or Simulate tasks to judge it on side A."
                            )
                        : ""
                );
            rate.MouseFilter = Control.MouseFilterEnum.Stop;
        }
        rate.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rate.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(rate);
        if (verdict == null)
            return box;
        var (word, colour) = verdict.Advice switch
        {
            // Translators: Advice on a slayer task: pay to extend it.
            TaskAdvice.Extend => (Tr.Get("Extend"), Look.Safe),
            // Translators: Advice on a slayer task: pay to reroll it.
            TaskAdvice.Reroll => (Tr.Get("Reroll"), Look.Risky),
            // Nothing to do: about the master's average, extended already, or not worth its cost.
            _ => ("–", Look.Style(TextStyle.Small).FontColor),
        };
        var advice = Ui.Label(word, TextStyle.BodyStrong);
        if (verdict.Advice == TaskAdvice.Keep)
        {
            advice.TooltipText =
                task.Extended ? Tr.Get("Extended already: the game extends a task once")
                : verdict.SameCoin ? Tr.Get("Neither extending nor rerolling earns back what it costs")
                : Tr.Get("About its master's overall rate: neither is worth paying for");
            advice.MouseFilter = Control.MouseFilterEnum.Stop;
        }
        Tint(advice, colour);
        advice.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(advice);
        // One statement after the advice: "for 🪙 3,325: +16,113 XP · Slayer over the 38 h the 334 kills left would take".
        var cost = Ui.Row(4);
        cost.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cost.SizeFlagsStretchRatio = 1.4f;
        cost.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(cost);
        if (verdict.Gain is { } gain)
        {
            // Translators: Between a task's advice (Extend, Reroll) and what it costs in the master's coin.
            cost.AddChild(Ui.Label(Tr.Get("for"), TextStyle.Small));
            if (coinOf(task.Currency) is { } coin)
                cost.AddChild(Ui.Picture(coin, 14));
            var text =
                verdict.Advice == TaskAdvice.Reroll
                    // Translators: A reroll's cost and gain; {1} the gain, {2} the goal's metric, {3} how long the
                    // kills left would take, {4} how many.
                    ? Tr.Format(
                        "{0}: +{1} {2} over the {3} the {4} kills left would take",
                        [
                            Ui.Number(verdict.Cost),
                            Ui.Number(gain),
                            unit,
                            Words.Duration(verdict.Hours * 3600),
                            verdict.Kills,
                        ]
                    )
                    // Translators: An extension's cost and gain; {1} the gain, {2} the goal's metric, {3} how long the
                    // kills added take, {4} how many.
                    : Tr.Format(
                        "{0}: +{1} {2} over the {3} of {4} more kills",
                        [
                            Ui.Number(verdict.Cost),
                            Ui.Number(gain),
                            unit,
                            Words.Duration(verdict.Hours * 3600),
                            verdict.Kills,
                        ]
                    );
            if (verdict.Net is { } net)
                // Translators: After a task's cost and gain, when both are the master's coin: what is left.
                text += " · " + Tr.Format("net +{0}", Ui.Number(net));
            var said = Ui.Clipped(text, TextStyle.Small);
            said.TooltipText =
                text
                + "\n\n"
                // Translators: Hover on a reroll's or extension's gain; {0} the master.
                + Tr.Format("Against the same time on {0}'s tasks", names(task.Master));
            said.MouseFilter = Control.MouseFilterEnum.Stop;
            cost.AddChild(said);
        }
        return box;
    }

    // "31% above Hans' overall rate", "about Kuren's overall rate": within the verdicts' 5% reads as about.
    private static string Against(double ratio, string master)
    {
        var percent = (int)Math.Round(Math.Abs(ratio - 1) * 100);
        return percent < TaskVerdicts.Material * 100
                // Translators: A task about as good as its master's rate over all its tasks; {0} is the master.
                ? Tr.Format("about {0}'s overall rate", master)
            : ratio > 1
                // Translators: A task better than its master's rate over all its tasks by {0} percent; {1} is the master.
                ? Tr.Format("{0}% above {1}'s overall rate", [percent, master])
            // Translators: A task worse than its master's rate over all its tasks by {0} percent; {1} is the master.
            : Tr.Format("{0}% below {1}'s overall rate", [percent, master]);
    }

    // Every master with a task, run on side A with the panel's trials.
    private void RunMasters()
    {
        if (LiveGame.Find() is not { } game)
            return;
        var targets = SlayerTasks.Current(game).Select(t => TargetSpec.FromKey(Ids.MasterKey(t.Master))).ToList();
        var prefs = PanelState.Prefs;
        try
        {
            RunService.Instance.Start(
                new RunRequest(
                    Simulate.SetupCheck.Current(),
                    targets,
                    prefs.Trials,
                    prefs.Seed,
                    prefs.Copies ?? RunService.Instance.Status.DefaultCopies
                )
            );
        }
        catch (Common.CommandException e)
        {
            _sub.Text = e.Message;
        }
        _shownFor = null;
    }

    private static void Tint(Label label, Color colour)
    {
        var settings = (LabelSettings)label.LabelSettings.Duplicate();
        settings.FontColor = colour;
        label.LabelSettings = settings;
    }
}
