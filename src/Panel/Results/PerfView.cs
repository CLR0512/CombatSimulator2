using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// A run's performance as lines over time: CPU (0–100%), the simulating process's memory and free memory (on one
/// memory scale) and the game's fps (0 to its highest). Each line in its metric's colour.
/// </summary>
internal static class PerfGraph
{
    public static Color Colour(PerfMetric metric) =>
        metric switch
        {
            PerfMetric.Cpu => Look.Engine,
            PerfMetric.Ram => Look.BarFill,
            PerfMetric.Free => Color.FromHtml("#a8a39b"),
            _ => Look.Best,
        };

    public static Control Make(PerfHistory history, Vector2 size)
    {
        var graph = new Control { CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        graph.Draw += () => Paint(graph, history);
        return graph;
    }

    /// <summary>The top of each line's scale.</summary>
    public static double Top(PerfHistory history, PerfMetric metric)
    {
        if (history.Samples.Count == 0)
            return 1;
        return metric switch
        {
            PerfMetric.Cpu => 100,
            PerfMetric.Fps => Math.Max(1, history.Series(PerfMetric.Fps).Max()),
            _ => Math.Max(1, Math.Max(history.Series(PerfMetric.Ram).Max(), history.Series(PerfMetric.Free).Max())),
        };
    }

    private static void Paint(Control graph, PerfHistory history)
    {
        var size = graph.Size;
        graph.PxRect(new Rect2(Vector2.Zero, size), Colors.Black with { A = 0.35f });
        for (var i = 1; i < 4; i++)
            graph.PxLine(
                new Vector2(0, size.Y * i / 4),
                new Vector2(size.X, size.Y * i / 4),
                Colors.White with
                {
                    A = 0.07f,
                }
            );
        var samples = history.Samples;
        if (samples.Count < 2)
            return;
        var start = samples[0].Seconds;
        var span = Math.Max(1e-6, samples[^1].Seconds - start);
        foreach (var metric in new[] { PerfMetric.Free, PerfMetric.Ram, PerfMetric.Fps, PerfMetric.Cpu })
        {
            var top = Top(history, metric);
            var points = samples
                .Select(s => new Vector2(
                    (float)((s.Seconds - start) / span * (size.X - 2) + 1),
                    (float)(size.Y - 2 - Math.Clamp(PerfHistory.Value(s, metric) / top, 0, 1) * (size.Y - 4))
                ))
                .ToArray();
            graph.DrawPolyline(points, Colour(metric), 2, true);
        }
    }
}

/// <summary>The run's cost as the results show it: the performance line, its hover, and the host status line.</summary>
internal static class PerfView
{
    /// <summary>
    /// The run's performance on hover: now, average, lowest and highest of each metric, and a graph over the run.
    /// Live during a run, else the last run's.
    /// </summary>
    public static void FillHover(VBoxContainer content, RunStatus status)
    {
        var history = status.CurrentPerf ?? status.LastPerf;
        if (history == null || history.Samples.Count == 0)
        {
            Empty(content, Tr.Get("Run a simulation to see its CPU, memory and frame rate here."));
            return;
        }
        var live = history == status.CurrentPerf;
        var title = (live, history.Where == RunService.InHost) switch
        {
            (true, true) => Tr.Get("Performance · this run, live · in the CS2 host"),
            (true, false) => Tr.Get("Performance · this run, live · in the game's process"),
            (false, true) => Tr.Get("Performance · last run · in the CS2 host"),
            (false, false) => Tr.Get("Performance · last run · in the game's process"),
        };
        Fill(content, history, title, live);
        if (RunService.Instance.Current is { } run && run.Id == history.RunId)
            content.AddChild(
                Ui.Label(run.IsFinal ? BuildAndSeed(run) + " · " + Ended(run) : BuildAndSeed(run), TextStyle.Tiny)
            );
    }

    /// <summary>
    /// A gear search's performance on hover, as a run's: live during a search, else the last search's.
    /// </summary>
    public static void FillSearchHover(VBoxContainer content, RunStatus status)
    {
        var history = status.SearchPerf ?? status.LastSearchPerf;
        if (history == null || history.Samples.Count == 0)
        {
            Empty(content, Tr.Get("Start a search to see its CPU, memory and frame rate here."));
            return;
        }
        var live = history == status.SearchPerf;
        var title = (live, history.Where == RunService.InHost) switch
        {
            (true, true) => Tr.Get("Performance · this search, live · in the CS2 host"),
            (true, false) => Tr.Get("Performance · this search, live · in the game's process"),
            (false, true) => Tr.Get("Performance · last search · in the CS2 host"),
            (false, false) => Tr.Get("Performance · last search · in the game's process"),
        };
        Fill(content, history, title, live);
    }

    private static void Empty(VBoxContainer content, string line)
    {
        content.AddChild(Ui.Label(Tr.Get("Performance"), TextStyle.BodyStrong));
        content.AddChild(Ui.Label(line, TextStyle.Small));
    }

    // Now, average, lowest and highest of each metric, and the graph over the whole history.
    private static void Fill(VBoxContainer content, PerfHistory history, string title, bool live)
    {
        content.AddChild(Ui.Label(title, TextStyle.BodyStrong));
        var grid = new GridContainer { Columns = 6, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 2);
        content.AddChild(grid);
        grid.AddChild(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        foreach (
            var head in new[]
            {
                "",
                live ? Tr.Get("now") : Tr.Get("at end"),
                Tr.Get("average"),
                Tr.Get("low"),
                Tr.Get("peak"),
            }
        )
            grid.AddChild(Ui.Label(head, TextStyle.Tiny));
        foreach (var metric in new[] { PerfMetric.Cpu, PerfMetric.Ram, PerfMetric.Free, PerfMetric.Fps })
        {
            if (history.Stats(metric) is not { } stats)
                continue;
            var swatch = new ColorRect
            {
                Color = PerfGraph.Colour(metric),
                CustomMinimumSize = new Vector2(12, 3),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            grid.AddChild(swatch);
            grid.AddChild(Ui.Label(PerfName(metric, history.Where), TextStyle.Small));
            foreach (var value in new[] { stats.Now, stats.Average, stats.Low, stats.Peak })
            {
                var cell = Ui.Label(PerfValue(metric, value), TextStyle.Small);
                cell.HorizontalAlignment = HorizontalAlignment.Right;
                grid.AddChild(cell);
            }
        }
        content.AddChild(PerfGraph.Make(history, new Vector2(380, 110)));
        var seconds = history.Samples[^1].Seconds - history.Samples[0].Seconds;
        content.AddChild(
            Ui.Label(
                Tr.Format(
                    "Over {0} s · CPU 0–100% · memory 0–{1} · fps 0–{2}",
                    [
                        seconds.ToString("0", Tr.Culture),
                        PerfValue(PerfMetric.Ram, PerfGraph.Top(history, PerfMetric.Ram)),
                        PerfValue(PerfMetric.Fps, PerfGraph.Top(history, PerfMetric.Fps)),
                    ]
                ),
                TextStyle.Tiny
            )
        );
    }

    // When a final run ended: "ran 3 min ago", "cancelled just now".
    private static string Ended(Run run)
    {
        var ago = Ago(run.EndedAt ?? run.StartedAt);
        return run.State switch
        {
            RunState.Cancelled => Tr.Format("cancelled {0}", ago),
            RunState.Error => Tr.Format("failed {0}", ago),
            _ => Tr.Format("ran {0}", ago),
        };
    }

    private static string Ago(DateTime utc)
    {
        var minutes = (int)Math.Max(0, (DateTime.UtcNow - utc).TotalMinutes);
        return minutes switch
        {
            0 => Tr.Get("just now"),
            < 60 => Tr.Plural("{0} min ago", "{0} min ago", minutes, minutes),
            < 60 * 24 => Tr.Plural("{0} h ago", "{0} h ago", minutes / 60, minutes / 60),
            _ => Tr.Plural("{0} d ago", "{0} d ago", minutes / 1440, minutes / 1440),
        };
    }

    /// <summary>The game build and seed a run was simulated with.</summary>
    public static string BuildAndSeed(Run run) =>
        Tr.Format(
            "build {0} · seed {1}",
            [run.GameBuild.Split(' ')[0], run.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture)]
        );

    private static string PerfName(PerfMetric metric, string where) =>
        metric switch
        {
            PerfMetric.Cpu => where == RunService.InHost ? Tr.Get("Simulator CPU") : Tr.Get("Game CPU"),
            PerfMetric.Ram => where == RunService.InHost ? Tr.Get("Simulator memory") : Tr.Get("Game memory"),
            PerfMetric.Free => Tr.Get("Free memory"),
            _ => Tr.Get("Game fps"),
        };

    private static string PerfValue(PerfMetric metric, double value) =>
        metric switch
        {
            PerfMetric.Cpu => Tr.Format("{0}%", value.ToString("0", Tr.Culture)),
            PerfMetric.Fps => value.ToString("0", Tr.Culture),
            _ => Ui.Bytes((long)value),
        };

    // What the simulator is doing, step by step, in the colour of its state (null: muted), and whether its dot
    // pulses. During a run: starting the simulator, building a copy, checking targets, fighting, finishing the last
    // targets. Between runs: getting copies ready, ready, or why the simulator failed.
    /// <summary>
    /// What <see cref="StatusLine"/> reads between runs, compared instead of the line itself: making the line
    /// allocates, and the panel asks whether it changed a few times a second.
    /// </summary>
    public readonly record struct StatusInputs(
        Run? Run,
        bool Final,
        HostState State,
        Note? Reason,
        bool Beat,
        string? Warm,
        int Stock,
        int StockTarget,
        bool InHost,
        bool JustEnded
    );

    public static StatusInputs Inputs(Run? run, RunService service)
    {
        var host = RunService.Host;
        var beat = host?.Reading;
        return new(
            run,
            run?.IsFinal ?? false,
            host?.State ?? HostState.NotStarted,
            host?.Reason,
            beat != null,
            beat?.Warm,
            beat?.Stock ?? 0,
            beat?.StockTarget ?? 0,
            PanelState.Prefs.SimulateIn == Preferences.InHost,
            service.Current is { IsFinal: true, EndedAt: { } ended } && (DateTime.UtcNow - ended).TotalSeconds < 60
        );
    }

    public static (string? Text, Color? Colour, bool Pulse) StatusLine(Run? run, RunService service)
    {
        var host = service.Status;
        var inHost = PanelState.Prefs.SimulateIn == Preferences.InHost;
        if (run != null && run == service.Current && !run.IsFinal)
        {
            if (run.SimulatedIn == RunService.InHost && host.HostState == HostState.Starting)
                return (Tr.Get("Starting the simulator…"), Look.Risky, true);
            return (RunStep(run, run.SimulatedIn == RunService.InHost ? host.Host?.Copies : null), Look.Engine, true);
        }
        // Starting, or up with no heartbeat yet.
        if (host.HostState == HostState.Starting || (host.HostState == HostState.Ready && host.Host == null && inHost))
            return (Tr.Get("Starting the simulator…"), Look.Risky, true);
        if (host.HostState == HostState.Failed && inHost)
            return (host.HostReason is { } reason ? Tr.Note(reason) : null, Look.Deadly, false);
        if (host.HostState != HostState.Ready || host.Host is not { } beat || !inHost)
            return (null, null, false);
        // Filling the stock of ready copies. Just after a run it only replaces the copies the run used up, in the
        // background, so it doesn't pulse; otherwise (the simulator just started) a run would wait on it.
        if (beat.Warm == "filling")
            return
                service.Current is { IsFinal: true, EndedAt: { } ended } && (DateTime.UtcNow - ended).TotalSeconds < 60
                ? (Tr.Format("Readying copies for the next run · {0} / {1}", beat.Stock, beat.StockTarget), null, false)
                : (
                    Tr.Format("Preparing the simulator · {0} / {1} copies", beat.Stock, beat.StockTarget),
                    Look.Engine,
                    true
                );
        // Up, but not yet told to fill its stock (the panel asks as it opens): still starting, as far as a run goes.
        if (beat.Warm == "none")
            return (Tr.Get("Starting the simulator…"), Look.Risky, true);
        if (beat.Warm == "ready" && beat.Stock > 0)
            return (
                Tr.Plural("Simulator ready · {0} copy", "Simulator ready · {0} copies", beat.Stock, beat.Stock),
                null,
                false
            );
        return (null, null, false);
    }

    // A target's own name, without its area: the status line is short.
    private static string ShortName(TargetResult result)
    {
        if (PanelState.Catalog?.Find(result.Key) is { } info)
            return info.Name;
        var name = Spoilers.Current.Name(result.Name);
        var open = name.IndexOf(" (", StringComparison.Ordinal);
        return open > 0 ? name[..open] : name;
    }

    // The step an active run is at, from its targets' states and the host's copies.
    private static string RunStep(Run run, int? hostCopies)
    {
        var queued = 0;
        var fighting = new List<TargetResult>();
        foreach (var key in run.Targets.Distinct(StringComparer.Ordinal))
        {
            switch (run.Results.GetValueOrDefault(key)?.State)
            {
                case TargetState.Queued:
                    queued++;
                    break;
                case TargetState.Fighting:
                    fighting.Add(run.Results[key]);
                    break;
            }
        }
        if (fighting.Count > 0)
        {
            var name = ShortName(fighting[0]);
            if (queued == 0 && run.Done > 0)
                return fighting.Count == 1
                    ? Tr.Format("Finishing the last target · {0}", name)
                    : Tr.Plural(
                        "Finishing the last {0} target",
                        "Finishing the last {0} targets",
                        fighting.Count,
                        fighting.Count
                    );
            return fighting.Count == 1
                ? Tr.Format("Fighting {0}", name)
                : Tr.Plural(
                    "Fighting {0} and {1} other",
                    "Fighting {0} and {1} others",
                    fighting.Count - 1,
                    name,
                    fighting.Count - 1
                );
        }
        // Nothing fighting yet: the first copy is being built or taken from the stock, then it checks every target.
        if (run.Done == 0 && hostCopies == 0)
            return Tr.Get("Building a copy of your game…");
        if (run.Done == 0)
            return Tr.Get("Checking which targets you can fight…");
        return Tr.Get("Starting the next targets…");
    }
}
