using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary><c>cs2dev</c>: developer checks, in Debug builds only.</summary>
internal static class Cs2DevCommand
{
    /// <summary>The dev subcommands: this table's own, then the compare and panel checks.</summary>
    public static List<Subcommand> Subs { get; } = [];

    /// <summary>
    /// Registers <c>cs2dev</c> with every dev subcommand, and the bench's frame sampling.
    /// </summary>
    internal static void Register(ModContext context)
    {
        context.OnFrame(Panel.Dev.FrameAlloc.After);
        context.OnFrame(Bench.Sample);
        context.OnFrame(BudgetsBench.OnFrame);
        CompareDevCommands.Add(Subs);
        UiDevCommands.Add(Subs);
        Subs.Add(ResultValues.Sub);
        Subs.Add(FitCommands.Sub);
        Subs.Add(SearchDev.Sub);
        Subs.Add(ReplayCommand.Sub);
        BuildLevers.Add(Subs);
        var commands = context.Commands;
        Subs.InsertRange(
            0,
            [
                new(
                    "guards",
                    "cs2dev guards",
                    [],
                    [],
                    Guards,
                    NeedsGame: false,
                    Purpose: "Guard hits, with the first stack of each"
                ),
                new(
                    "setup-state",
                    "cs2dev setup-state",
                    [],
                    [],
                    SetupStateCommand,
                    Purpose: "The live setup and its hash"
                ),
                new(
                    "live-state",
                    "cs2dev live-state",
                    [],
                    [],
                    LiveStateCommand,
                    Purpose: "A hash of everything in the live game a change could touch: equal before and after a check or a run"
                ),
                new(
                    "scale",
                    "cs2dev scale [0.5-3]",
                    [],
                    [],
                    WindowScaleCommand,
                    NeedsGame: false,
                    Purpose: "Set what 100% means live, not saved"
                ),
                new(
                    "fingerprint",
                    "cs2dev fingerprint [label] | cs2dev fingerprint diff <fp-a> <fp-b> | cs2dev fingerprint diff <Entity.Component> [seconds=1.5]",
                    ["seconds"],
                    [],
                    Fingerprint,
                    Purpose: "Take a fingerprint of the live game, or compare two; diff with one component: write it twice that far apart and list the bytes that changed"
                ),
                new(
                    "observe",
                    "cs2dev observe start|stop|result",
                    [],
                    [],
                    ObserveCommand,
                    Purpose: "Watch live play, then simulate it and compare"
                ),
                new(
                    "bench",
                    "cs2dev bench [seconds=20] [copies=<n>] [target=<key>] [where=host|game] [cold] | cs2dev bench idle [seconds=10] | cs2dev bench budgets [only=<name,…>] [repeats=3] [seconds=10] [look=base|melvui] | cs2dev bench result | cs2dev bench stop",
                    ["seconds", "copies", "target", "trials", "where", "only", "repeats", "look"],
                    ["cold", "low-latency"],
                    BenchCommand,
                    NeedsGame: false,
                    Purpose: "Frame-rate bench, with or without a run; budgets: every budget measured and judged in one sequence (groups: closed, panel, smoothness, memory)"
                ),
                new(
                    "timings",
                    "cs2dev timings [run id]",
                    [],
                    [],
                    Timings,
                    NeedsGame: false,
                    Purpose: "Every timing a run recorded; for the newest run, also what each fight slot did"
                ),
                new(
                    "reuse",
                    "cs2dev reuse on|off",
                    [],
                    [],
                    Reuse,
                    NeedsGame: false,
                    Purpose: "Reuse copies between targets, or not"
                ),
                new(
                    "reset",
                    "cs2dev reset <target…|all> [setup=<loadout>] [ignore-req] [trials=1000] | cs2dev reset result",
                    ["trials", "setup"],
                    ["ignore-req"],
                    ResetCheck.Run,
                    Purpose: "Fight targets in one copy and list what leaks between them"
                ),
                new(
                    "memory",
                    "cs2dev memory [collect]",
                    [],
                    [],
                    GameMemory.Run,
                    NeedsGame: false,
                    Purpose: "The game process's own memory and heap, and the CS2 copies hot reloads have left; collect: after a full collection"
                ),
                new(
                    "batches",
                    "cs2dev batches on|off|dump",
                    [],
                    [],
                    HostLevers.Batches,
                    NeedsGame: false,
                    Purpose: "Record what each copy fought in the host, in order, with a hash of every batch: on starts a record, dump writes it to a file, off ends it"
                ),
                new(
                    "host",
                    "cs2dev host [status|start|warm|stock|collect|env <NAME>:<value>…|env clear|kill|hang|crash|throw [build|thread|<target>]|failfast|overflow|av|fail-start|mismatch|stop|reserve=<GB>|idle=<s>|idle-open=<s>|release=<s>|cores=<n>|standins=off|on|stock=<n>]",
                    ["reserve", "idle", "idle-open", "release", "cores", "standins", "stock"],
                    [],
                    HostLevers.Run,
                    NeedsGame: false,
                    Purpose: "Host handshake, heartbeat, memory (heap, native, a run's peak) and warm stock; collect: one full collection, then status says what it freed; fault injection (throw, throw build: one target fails and the run goes on; throw thread, failfast, overflow, av: each way the host can die, with no box); reserve, idle stop (idle: panel closed, idle-open: panel open) and warm-stock delay; cores: the next host as on a machine with that many cores (0: this machine's); standins=off: the next host loads textures and sounds; stock=<n>: bare copies the next warm-ups ask for (0: the default)"
                ),
            ]
        );
        var table = new SubcommandTable("cs2dev", Subs);
        commands.Register(
            "cs2dev",
            "cs2dev <subcommand> …",
            "Combat Simulator 2 developer checks",
            table.Handle,
            dev: true
        );
    }

    private static void Fingerprint(CommandCall call, CommandArgs args)
    {
        if (args.Word(0) == "diff")
        {
            FingerprintDiff(call, args);
            return;
        }
        var fingerprint = Fingerprints.Take(LiveGame.Require(), args.Word(0));
        call.Ok($"{fingerprint.Id} {fingerprint.Parts.Count} parts frame {fingerprint.Frame}");
    }

    private static void FingerprintDiff(CommandCall call, CommandArgs args)
    {
        const string Usage =
            "usage: cs2dev fingerprint diff <fp-a> <fp-b> | cs2dev fingerprint diff <Entity.Component> [seconds=1.5]";
        if (args.Word(1) is { } component && args.Word(2) is null)
        {
            ComponentDiff(call, component, args.Double("seconds", 0.1, 60) ?? 1.5);
            return;
        }
        var a = Fingerprints.Get(args.Word(1) ?? "") ?? throw new CommandException(Usage);
        var b = Fingerprints.Get(args.Word(2) ?? "") ?? throw new CommandException(Usage);
        var parts = Fingerprints.Diff(a, b);
        call.Ok($"{parts.Count} parts differ" + string.Concat(parts.Select(p => "\n" + p)));
    }

    // Writes one component of the live game now and again after the wait, and lists the bytes that changed.
    private static void ComponentDiff(CommandCall call, string component, double seconds)
    {
        var first =
            Fingerprints.ComponentBytes(LiveGame.Require(), component)
            ?? throw new CommandException($"no component {component} (as in a fingerprint part: Combat.Player)");
        var wait = System.Diagnostics.Stopwatch.StartNew();
        MainThread.When(
            () => wait.Elapsed.TotalSeconds >= seconds,
            () =>
            {
                var second = LiveGame.Find() is { } game ? Fingerprints.ComponentBytes(game, component) : null;
                if (second == null)
                {
                    call.Error($"{component} is gone: the game was left");
                    return;
                }
                var ranges = Fingerprints.ByteRanges(first, second);
                call.Ok(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{component}: {first.Length} bytes, {ranges.Count} ranges differ after {wait.Elapsed.TotalSeconds:0.00} s"
                    ) + string.Concat(ranges.Take(60).Select(r => "\n" + r))
                );
            },
            TimeSpan.FromSeconds(seconds + 10),
            () => call.Error("the second write never ran")
        );
    }

    private static void ObserveCommand(CommandCall call, CommandArgs args)
    {
        switch (args.Word(0))
        {
            case "start":
                call.Ok(Engine.Dev.Observe.Start());
                break;
            case "stop":
                call.Ok(Engine.Dev.Observe.Stop());
                break;
            case "result":
                call.Ok(Engine.Dev.Observe.Result());
                break;
            default:
                throw new CommandException("usage: cs2dev observe start|stop|result");
        }
    }

    private static void BenchCommand(CommandCall call, CommandArgs args)
    {
        switch (args.Word(0))
        {
            case "result":
                call.Ok(_budgetsLast ? BudgetsBench.Result() : Bench.Result());
                return;
            case "stop":
                BudgetsBench.Stop();
                Bench.Stop();
                call.Ok(_budgetsLast ? BudgetsBench.Result() : Bench.Result());
                return;
            case "budgets":
                BenchBudgets(call, args);
                return;
            case "idle":
                _budgetsLast = false;
                BenchIdle(call, args);
                return;
        }
        if (BudgetsBench.Running)
            throw new CommandException($"{BudgetsBench.Id} is still running");
        _budgetsLast = false;
        var game = LiveGame.Require();
        var target = Targets.Resolve(game, args.String("target") ?? "GolbinForest/Golbin");
        var copies = args.Int("copies", Engine.DefaultCopies.Now, 1, Math.Max(1, System.Environment.ProcessorCount));
        var seconds = args.Int("seconds", 20, 5, 120);
        var where = RunArgs.Where(args) ?? RunService.DefaultWhere();
        Bench.Start(
            target,
            copies,
            seconds,
            where,
            args.Flag("cold"),
            args.Flag("low-latency"),
            args.Int("trials", 1000, 0, 10_000_000)
        );
        call.Ok(
            $"{Bench.Id} started in {where}: {seconds} s control, then {seconds} s on {copies} copies (cs2dev bench result)"
        );
    }

    /// <summary>CS2 is unloading: a bench in progress ends now and puts back what it changed.</summary>
    internal static void Unloading()
    {
        BudgetsBench.Stop();
        Bench.Stop();
    }

    // Which bench "bench result" reads: the budgets sequence or the run bench, whichever was started last.
    private static bool _budgetsLast;

    private static void BenchBudgets(CommandCall call, CommandArgs args)
    {
        var only =
            args.String("only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        var budgets = Engine.Dev.Budgets.Select(only, out var unknown);
        if (unknown.Count > 0)
            throw new CommandException(
                $"no budget or group '{string.Join("', '", unknown)}' (groups: {string.Join(", ", Engine.Dev.Budgets.Groups)})"
            );
        string[] looks = args.String("look") switch
        {
            null => [SkinChoice.Base, SkinChoice.MElvUI],
            SkinChoice.Base => [SkinChoice.Base],
            SkinChoice.MElvUI => [SkinChoice.MElvUI],
            var other => throw new CommandException($"look={other}: base or melvui"),
        };
        BudgetsBench.Start(budgets, args.Int("repeats", 3, 1, 9), args.Int("seconds", 10, 2, 120), looks);
        _budgetsLast = true;
        call.Ok($"{BudgetsBench.Id} started: {budgets.Count} budgets (cs2dev bench result, cs2dev bench stop)");
    }

    private static void Guards(CommandCall call, CommandArgs args)
    {
        var events = GuardLog.Snapshot();
        var blocked = events.Count(e => e.Action == GuardAction.Blocked);
        var text = new StringBuilder($"{events.Count} guards hit, {blocked} blocked");
        foreach (var e in events)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{ResultText.Name(e.Action)} {e.Guard} ×{e.Count}");
            if (e.FirstStack != null)
                text.Append("\n  ").Append(e.FirstStack.Replace("\n", "\n  ", StringComparison.Ordinal));
        }
        call.Ok(text.ToString());
    }

    // A frame-rate sample with no run.
    private static void BenchIdle(CommandCall call, CommandArgs args)
    {
        var seconds = args.Int("seconds", 10, 1, 120);
        Bench.Measure(seconds);
        call.Ok($"{Bench.Id} sampling {seconds} s (cs2dev bench result)");
    }

    private static void Timings(CommandCall call, CommandArgs args)
    {
        var id = args.Word(0);
        var run = RunFinder.CurrentOrSaved(id) ?? throw new CommandException("no such run");
        var t = run.Timings ?? throw new CommandException($"{run.Id} has no timings");
        var text = new StringBuilder(run.Id).Append('\n').Append(RunTimingsText.Line(t));
        foreach (var property in typeof(Model.RunTimings).GetProperties())
            text.Append(CultureInfo.InvariantCulture, $"\n{property.Name} {property.GetValue(t):0.####}");
        // The slots are the newest pool run's, wherever it ran: they aren't saved with a run.
        if (id == null)
        {
            foreach (var line in Engine.Dev.SlotLog.Newest(Host.HostClient.DataRoot))
                text.Append('\n').Append(line);
        }
        call.Ok(text.ToString());
    }

    private static void Reuse(CommandCall call, CommandArgs args)
    {
        RunService.SetReuseCopies(
            args.Word(0) switch
            {
                "on" => true,
                "off" => false,
                _ => throw new CommandException("usage: cs2dev reuse on|off"),
            }
        );
        call.Ok($"copy reuse {(RunService.ReuseCopies ? "on" : "off")} for the next runs");
    }

    // What 100% means, tried live: it is picked by eye.
    private static void WindowScaleCommand(CommandCall call, CommandArgs args)
    {
        if (args.Word(0) is { } word)
        {
            if (!double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new CommandException($"not a scale: '{word}' (0.5 to 3)");
            WindowScale.Base = (float)value;
        }
        call.Ok(
            string.Create(CultureInfo.InvariantCulture, $"base {WindowScale.Base:0.00} drawn {Cs2Modal.DrawnNow:0.00}")
        );
    }

    // The live game's levels, gear, food, prayers and potion with a hash: equal before and after a what-if check
    // shows the check left the live game untouched.
    private static void LiveStateCommand(CommandCall call, CommandArgs args)
    {
        var lines = LiveGame.StateLines(LiveGame.Require());
        call.Ok("hash " + SetupState.Hash(lines) + "\n" + string.Join("\n", lines));
    }

    private static void SetupStateCommand(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var lines = SetupState.Read(game, LiveGame.Combat(game)).Lines();
        call.Ok("hash " + SetupState.Hash(lines) + "\n" + string.Join("\n", lines));
    }
}
