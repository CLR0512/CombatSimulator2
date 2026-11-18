using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary><c>cs2 run</c>, <c>status</c>, <c>wait</c>, <c>cancel</c> and <c>result</c>.</summary>
internal static class Cs2RunCommands
{
    public static void Add(List<Subcommand> subs) =>
        subs.AddRange([
            new(
                "run",
                "cs2 run <target…|current|selection|all> [setup=<id>] [change…] [ignore-req] [trials=1000] [seed=<u64>] [copies=<n>] [where=host|game] [fresh] [stop=<percent>]"
                    + "\ncs2 run rename <run id> [name…] | cs2 run pin|unpin <run id> | cs2 run delete <run id|file> | cs2 run show <run id>",
                ["setup", "trials", "seed", "copies", "where", "stop", .. ChangeArgs.Options],
                ["ignore-req", "fresh"],
                Run
            ),
            new(
                "targets",
                "cs2 targets [filter] [setup=<id>] [change…] [ignore-req]",
                ["setup", .. ChangeArgs.Options],
                ["ignore-req"],
                TargetList
            ),
            new("status", "cs2 status [run id]", [], [], Status),
            new("wait", $"cs2 wait [timeout=<s>, up to {MaxWaitSeconds}]", ["timeout"], [], Wait),
            new("runs", "cs2 runs | cs2 runs clear [confirm]", [], [], Runs),
            new("cancel", "cs2 cancel [all]", [], [], Cancel),
            new(
                "result",
                "cs2 result [run id] [target] [metric=<key>] [unit=kill|second|minute|hour|day] [full]",
                ["unit", "metric"],
                ["full"],
                Result
            ),
        ]);

    private static void Run(CommandCall call, CommandArgs args)
    {
        if (args.Words.Count == 0)
            throw new CommandException("usage: cs2 run <target…|current|selection|all> (see cs2 help)");
        // No target key is one of these words, so they name the kept-run actions.
        switch (args.Words[0].ToLowerInvariant())
        {
            case "rename":
                // The name as typed: words the parser took as flags are still part of it.
                var name = string.Join(' ', call.Args.Skip(3));
                call.Ok(HeaderLine(RunService.Instance.RenameRun(RunId(args, "rename <run id> [name…]"), name)));
                return;
            case "pin":
            case "unpin":
                var pin = args.Words[0].Equals("pin", StringComparison.OrdinalIgnoreCase);
                call.Ok(HeaderLine(RunService.Instance.PinRun(RunId(args, args.Words[0] + " <run id>"), pin)));
                return;
            case "delete":
                var id = RunId(args, "delete <run id|file>");
                if (RunService.Instance.DeleteRun(id) is { } refused)
                    throw new CommandException($"{id} not deleted: {refused.English()}");
                call.Ok($"{id} deleted");
                return;
            case "show":
                ShowSaved(call, RunId(args, "show <run id>"));
                return;
        }
        var setup = RunArgs.SetupFrom(args);
        var (changes, onB) = ChangeArgs.From(args, SideLetter.A);
        if (!onB.IsEmpty)
            throw new CommandException("a single run has one side: give its changes without b. (or use cs2 compare)");
        var request = RunArgs.RequestFrom(args, setup, args.Words);
        if (changes.IsEmpty)
        {
            Started(call, RunService.Instance.Start(request), "");
            return;
        }
        setup.Changes = changes;
        RunService.Instance.StartSides(
            request,
            null,
            start => Started(call, start.Run!, SideText.Checks(start.A, null) + "\n"),
            error => call.Error(error.Message)
        );
    }

    // Replies once the run is known to be going: a host that can't start fails it within a few seconds.
    private static void Started(CommandCall call, Run run, string before)
    {
        var summary =
            before
            + $"{run.Id} started {run.Targets.Count} targets seed {run.Seed} copies {run.Copies} setup {Ids.Brief(run.Setup.Loadout)} in {run.SimulatedIn}";
        if (run.Timings is { Reused: > 0 } t)
            summary += $" reused {t.Reused}";
        if (run.SimulatedIn != RunService.InHost || run.IsFinal)
        {
            call.Ok(summary);
            return;
        }
        var host = HostClient.Instance;
        MainThread.When(
            () => run.IsFinal || host.State is HostState.Ready or HostState.Busy,
            () =>
            {
                if (run.State == RunState.Error)
                    call.Error($"host unavailable: {run.Error?.English()}");
                else
                    call.Ok(summary);
            },
            TimeSpan.FromSeconds(8),
            () => call.Ok(summary + " (simulator still starting)")
        );
    }

    private static string RunId(CommandArgs args, string usage) =>
        args.Word(1) ?? throw new CommandException($"usage: cs2 run {usage} (see cs2 runs)");

    private static void ShowSaved(CommandCall call, string id)
    {
        var warnings = RunService.Instance.ShowSaved(id);
        PanelState.SetCompareWith(null); // the results view shows the run, not the comparison
        var run = RunService.Instance.Current!;
        var text = new StringBuilder();
        foreach (var warning in warnings ?? [])
            text.Append("warning: ").Append(warning.English()).Append('\n');
        text.Append(
            CultureInfo.InvariantCulture,
            $"{run.Id} shown: {ResultText.Name(run.State)} {run.Done}/{run.Targets.Count} targets setup {run.Setup.Label} build {run.GameBuild} seed {run.Seed}"
        );
        call.Ok(text.ToString());
    }

    /// <summary>A kept run on one line, as <c>cs2 runs</c> lists it.</summary>
    private static string HeaderLine(RunHeader h)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{h.Id} {ResultText.Name(h.State)} {h.Setup}{(h.IgnoreRequirements ? Setup.RequirementsIgnoredSuffix : "")} build {h.GameBuild} seed {h.Seed} {h.Targets} targets"
        );
        if (h.Name != null)
            line += $" \"{h.Name}\"";
        return h.Pinned ? line + " pinned" : line;
    }

    private static void TargetList(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var setup = RunArgs.SetupFrom(args);
        setup.Changes = ChangeArgs.From(args, SideLetter.A).A;
        var slot = setup.Loadout == Setup.Current ? null : setup.Loadout;
        if (slot != null && LiveGame.Loadouts(game).All(l => l.Slot != slot))
            throw new CommandException($"no loadout '{Ids.Brief(slot)}' (see cs2 setups)");
        var filter = args.Word(0);
        MainThreadReply.ReplyWhen<TargetCatalog>(
            call,
            done => TargetCatalog.Check(game, setup, slot, done),
            (catalog, error) =>
            {
                if (catalog == null)
                {
                    call.Error($"check failed: {error?.GetType().Name}: {error?.Message}");
                    return;
                }
                call.Ok(Listing(catalog, filter, setup.IgnoreRequirements));
            },
            TimeSpan.FromSeconds(9),
            "the check copy took longer than 9 s"
        );
    }

    private static string Listing(TargetCatalog catalog, string? filter, bool ignoreRequirements)
    {
        var shown = catalog
            .List.Where(t =>
                filter == null
                || t.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || t.Group.Contains(filter, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        var eligible = shown.Count(t => t.Eligible || ignoreRequirements);
        var text = new StringBuilder($"{shown.Count} targets, {eligible} eligible");
        foreach (var t in shown)
        {
            var kind = t.Spec.Kind switch
            {
                TargetKind.Dungeon => "dungeon",
                TargetKind.Master => "master",
                _ => "enemy",
            };
            var state =
                t.Eligible ? "eligible"
                : ignoreRequirements ? "eligible (requirements ignored)  " + t.Reasons.English(" | ")
                : "refused  " + t.Reasons.English(" | ");
            text.Append(
                CultureInfo.InvariantCulture,
                $"\n{kind, -8}{Ids.BriefKey(t.Key), -40}  {t.Group, -44}  {state}"
            );
        }
        return text.ToString();
    }

    private static void Status(CommandCall call, CommandArgs args)
    {
        var service = RunService.Instance;
        var host = HostStatusText.Of(HostClient.Instance);
        if (service.FailureLog.Newest() is [var last, ..])
            host += $"\nlast failure: {FailureRecord.Name(last.Kind)} {last.RunId ?? "-"} {last.Reason.English()}";
        if (args.Word(0) == null && service.Current == null)
        {
            call.Ok(LiveGame.Checked.StatusLine + "\nno run yet\n" + host);
            return;
        }
        var run = RunFinder.Find(args.Word(0));
        var seconds = run == service.Current && !run.IsFinal ? service.RealSeconds : Seconds(run);
        // First, so a reply read alone still says whether its results come from a game CS2 was checked with.
        var text = new StringBuilder(LiveGame.Checked.StatusLine).Append('\n');
        text.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{run.Id} {ResultText.Name(run.State)} {run.Done}/{run.Targets.Count} targets {seconds:0.0} s in {run.SimulatedIn ?? RunService.InGame}"
            )
        );
        if (run.Error != null)
            text.Append(": ").Append(run.Error.English());
        text.Append('\n').Append(host);
        text.Append(
            CultureInfo.InvariantCulture,
            $"\nmain skill {(run.MainSkill is { } skill ? Ids.Brief(skill) : "unknown")} (ranks {Ids.Brief(Metrics.Resolve(run, Metrics.MainXp))})"
        );
        foreach (var key in run.Targets)
        {
            if (run.Results[key] is { IsFinal: false } result)
            {
                text.Append('\n').Append(Ids.BriefKey(key)).Append(' ').Append(ResultText.Name(result.State));
                if (result.State == TargetState.Fighting)
                    text.Append(CultureInfo.InvariantCulture, $" {result.TrialsDone}/{run.Trials}");
            }
        }
        foreach (var e in GuardLog.Snapshot())
            text.Append(CultureInfo.InvariantCulture, $"\nguard {ResultText.Name(e.Action)} {e.Guard} ×{e.Count}");
        if (run == service.Current && service.LastStats is { } stats)
            text.Append(
                CultureInfo.InvariantCulture,
                $"\npool peak {stats.PeakCopies} copies, built {stats.Built} ({stats.PreBuiltUsed} ahead)"
            );
        if (run.IsFinal && run.Timings is { } t)
            text.Append('\n').Append(RunTimingsText.Line(t));
        call.Ok(text.ToString());
    }

    // The bridge stops a command after 10 s, so a wait ends before that; a script repeats it to wait longer.
    private const int MaxWaitSeconds = 9;

    // Replies once no run or comparison is in progress, and fails when the timeout passes first, so a batch stops
    // before it reads a result that is still coming.
    private static void Wait(CommandCall call, CommandArgs args)
    {
        var service = RunService.Instance;
        var timeout = args.Double("timeout", 0, MaxWaitSeconds) ?? MaxWaitSeconds;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        MainThread.When(
            () => !service.IsActive,
            () =>
                call.Ok(
                    service.Current is { } run
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"idle after {watch.Elapsed.TotalSeconds:0.0} s: {run.Id} {ResultText.Name(run.State)} {run.Done}/{run.Targets.Count} targets"
                        )
                        : "idle: no run yet"
                ),
            TimeSpan.FromSeconds(timeout),
            () =>
                call.Error(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"still running after {timeout:0.#} s: {service.Current?.Id} {service.Current?.Done}/{service.Current?.Targets.Count} targets"
                    )
                )
        );
    }

    private static double Seconds(Run run) => ((run.EndedAt ?? DateTime.UtcNow) - run.StartedAt).TotalSeconds;

    private static void Runs(CommandCall call, CommandArgs args)
    {
        if (args.Word(0) is { } word)
        {
            if (!word.Equals("clear", StringComparison.OrdinalIgnoreCase))
                throw new CommandException("usage: cs2 runs | cs2 runs clear [confirm]");
            call.Ok(Clear(args.Word(1)));
            return;
        }
        var store = RunStore.Shared;
        var (headers, unreadable) = store.ListAll();
        var text = new StringBuilder($"{headers.Count} saved (max {store.Max})");
        foreach (var h in headers)
            text.Append('\n').Append(HeaderLine(h));
        foreach (var file in unreadable)
        {
            var why = file.Why.StartsWith(file.File + ": ", StringComparison.Ordinal)
                ? file.Why[(file.File.Length + 2)..]
                : file.Why;
            text.Append(CultureInfo.InvariantCulture, $"\nunreadable {file.File}: {why}");
        }
        var comparison = store.Pinned();
        var pinned = headers.Count(h => h.Pinned || comparison.Contains(h.Id));
        text.Append(CultureInfo.InvariantCulture, $"\nkept {headers.Count} of {store.Max} ({pinned} pinned)");
        call.Ok(text.ToString());
    }

    // What clearing the saved runs would do, or, with confirm, clearing them: the counts are the plan's either way.
    private static string Clear(string? confirm)
    {
        var service = RunService.Instance;
        var plan = service.ClearPlan();
        var counts = string.Create(
            CultureInfo.InvariantCulture,
            $"{plan.Delete.Count} runs and {plan.ComparisonsDelete.Count} comparisons; {plan.PinnedKept} pinned runs and {plan.ComparisonsKept} comparisons stay"
        );
        if (confirm == null)
            return "would delete " + counts;
        if (!confirm.Equals("confirm", StringComparison.OrdinalIgnoreCase))
            throw new CommandException("usage: cs2 runs | cs2 runs clear [confirm]");
        service.Clear(plan);
        return "deleted " + counts;
    }

    private static void Cancel(CommandCall call, CommandArgs args)
    {
        if (args.Words is ["all"])
        {
            call.Ok(CancelAll(RunService.Instance));
            return;
        }
        var (run, ms, finished, cancelled) = RunService.Instance.Cancel();
        call.Ok($"{run.Id} cancelled in {Fmt.Ms(ms)} ms, {finished} finished, {cancelled} cancelled");
    }

    // Whatever is going stops, and nothing going is no error, so a check's teardown can always end with it and one
    // check left running never fails the ones after it.
    private static string CancelAll(RunService service)
    {
        var stopped = new List<string>();
        if (service.Search is { IsActive: true })
        {
            service.CancelSearch();
            stopped.Add("gear search");
        }
        if (service.BestLoadouts is { IsActive: true })
        {
            service.CancelBestLoadouts();
            stopped.Add("best loadouts");
        }
        if (service.IsActive)
            stopped.Add(service.Cancel().Run.Id);
        return stopped.Count == 0 ? "nothing was running" : "cancelled " + string.Join(", ", stopped);
    }

    private static void Result(CommandCall call, CommandArgs args)
    {
        var unit = args.Unit("unit", TimeUnit.Hour);
        string? id = null;
        string? key = null;
        foreach (var word in args.Words)
        {
            if (word.StartsWith("run-", StringComparison.Ordinal))
                id = word;
            else
                key = word;
        }
        var run = RunFinder.Find(id);
        var game = LiveGame.Require();
        var prices = LiveGame.Prices(game);
        Func<string, string> names = i => LiveGame.NameOf(game, i);
        if (key == null)
        {
            call.Ok(ResultText.Summary(run, unit, prices, args.String("metric") ?? PanelState.Prefs.Metric));
            return;
        }
        var full = Ids.FullKey(key);
        var result =
            run.Results.GetValueOrDefault(full) ?? throw new CommandException($"{run.Id} has no target '{key}'");
        call.Ok(
            ResultText.Details(
                run,
                result,
                unit,
                prices,
                names,
                args.Flag("full"),
                args.String("metric") ?? PanelState.Prefs.Metric
            )
        );
    }
}
