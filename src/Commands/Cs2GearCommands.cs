using System.Globalization;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary><c>cs2 gear</c>: find the best gear the character owns for one target, and print the answer.</summary>
internal static class Cs2GearCommands
{
    // The bridge stops a command after 10 s, so a reply that waits ends before that; cs2 wait repeats the wait.
    private const int MaxWaitSeconds = 9;

    private const string Usage =
        "cs2 gear <target> [metric=<key>] [deaths=none|5|any] [lock=<slot>,…] [exclude=<item>,…] [styles=all] "
        + "[trials=] [seed=] [copies=] [where=host|game] [wait=<s>] [short] [setup=<loadout>] [ignore-req] "
        + "[kinds=<kind>,…|all|none] [unowned] "
        + PoolArgs.Usage
        + " [<change>…] "
        + "| cs2 gear plan <target> … | cs2 gear show [target] | cs2 gear cancel | cs2 gear open [tie=<n>|alt=cheaper|safer] "
        + "| cs2 gear keep [tie=<n>]";

    private const string DropUsage =
        "cs2 droptimizer <target> [metric=<key>] [deaths=none|5|any] [exclude=<item>,…] [trials=] [seed=] [copies=] "
        + "[where=host|game] [wait=<s>] [setup=<loadout>] [ignore-req] [<change>…] "
        + "| cs2 droptimizer show [target] [full] | cs2 droptimizer cancel | cs2 droptimizer open item=<id>";

    public static void Add(List<Subcommand> subs)
    {
        subs.Add(
            new(
                "gear",
                Usage,
                [
                    "metric",
                    "deaths",
                    "lock",
                    "exclude",
                    "styles",
                    "trials",
                    "seed",
                    "copies",
                    "where",
                    "wait",
                    "setup",
                    "tie",
                    "alt",
                    "kinds",
                    .. PoolArgs.Options,
                    .. ChangeArgs.Options,
                ],
                ["short", "ignore-req", "unowned", .. PoolArgs.Flags],
                Gear,
                Purpose: "Finds the best owned gear for one target: the best set, the sets as good, and the gain over current gear"
            )
        );
        subs.Add(
            new(
                "droptimizer",
                DropUsage,
                [
                    "metric",
                    "deaths",
                    "lock",
                    "exclude",
                    "styles",
                    "trials",
                    "seed",
                    "copies",
                    "where",
                    "wait",
                    "setup",
                    "item",
                    .. ChangeArgs.Options,
                ],
                ["full", "ignore-req"],
                Droptimizer,
                Purpose: "Lists the items the character doesn't own by what each would gain, swapped alone into current gear"
            )
        );
    }

    // The comparison's id is side B's run, which a comparison over several targets starts once A has finished.
    private static string Opened(ComparedRuns comparison) =>
        comparison.BStarted
            ? $"{comparison.Record.Id} {(comparison.IsFinal ? "final at once" : "started")}: A {comparison.A.Id}, B {comparison.B.Id}, seed {comparison.Record.Seed}"
            : $"started: A {comparison.A.Id}, B waiting, seed {comparison.Record.Seed}";

    private static void Droptimizer(CommandCall call, CommandArgs args)
    {
        var service = RunService.Instance;
        switch (args.Word(0)?.ToLowerInvariant())
        {
            case null:
                throw new CommandException("usage: " + DropUsage);
            case "show":
                Show(call, args, service, SearchKind.Droptimizer);
                return;
            case "cancel":
                var cancelled = service.CancelSearch();
                call.Ok(GearText.OfDrops(cancelled.Answer!).Split('\n')[^1]);
                return;
            case "open":
                var item = Model.Ids.Full(
                    args.String("item") ?? throw new CommandException("usage: cs2 droptimizer open item=<id>")
                );
                service.OpenAnswer(
                    null,
                    start =>
                        call.Ok(
                            start.Comparison is { } comparison
                                ? Opened(comparison)
                                : $"the swap changes nothing: {start.Run!.Id} started as a single run"
                        ),
                    error => call.Error(error.Message),
                    item
                );
                return;
        }
        var request = RequestFrom(args, args.Word(0)!, SearchKind.Droptimizer);
        var search = service.StartSearch(request);
        var wait = args.Double("wait", 0, MaxWaitSeconds) ?? MaxWaitSeconds;
        MainThread.When(
            () => !search.IsActive,
            () => call.Ok(GearText.OfDrops(search.Answer!)),
            TimeSpan.FromSeconds(wait),
            () => call.Ok(Running(search))
        );
    }

    private static void Gear(CommandCall call, CommandArgs args)
    {
        var service = RunService.Instance;
        switch (args.Word(0)?.ToLowerInvariant())
        {
            case null:
                throw new CommandException("usage: " + Usage);
            case "show":
                Show(call, args, service, SearchKind.BestGear);
                return;
            case "plan":
                Plan(call, args);
                return;
            case "keep":
                var kept = service.KeepAnswer(Tie(args));
                call.Ok(
                    $"{kept.Id} kept: {Model.Ids.BriefKey(kept.Targets[0])}, {kept.Setup.Changes?.ShortForm ?? "current gear"}"
                );
                return;
            case "open":
                service.OpenAnswer(
                    Tie(args),
                    start =>
                        call.Ok(
                            start.Comparison is { } comparison
                                ? Opened(comparison)
                                : $"the set is current gear: {start.Run!.Id} started as a single run"
                        ),
                    error => call.Error(error.Message),
                    null,
                    args.String("alt")?.ToLowerInvariant()
                );
                return;
            case "cancel":
                var cancelled = service.CancelSearch().Answer!;
                // A stop that kept a set says what it kept; otherwise how the search ended.
                call.Ok(
                    cancelled.StoppedAtStage != null ? GearText.Of(cancelled) : GearText.Of(cancelled).Split('\n')[^1]
                );
                return;
        }
        var request = RequestFrom(args, args.Word(0)!);
        var leftOut = LeftOut(args.Word(0)!);
        var search = service.StartSearch(request);
        var wait = args.Double("wait", 0, MaxWaitSeconds) ?? MaxWaitSeconds;
        MainThread.When(
            () => !search.IsActive,
            () => call.Ok(leftOut + GearText.Of(search.Answer!)),
            TimeSpan.FromSeconds(wait),
            () => call.Ok(leftOut + Running(search))
        );
    }

    // What would be fought, without fighting: the counts as an answer's first lines give them, and how long the
    // search is expected to take when one of this kind of target has been timed.
    private static void Plan(CommandCall call, CommandArgs args)
    {
        var request = RequestFrom(args, args.Word(1) ?? throw new CommandException("usage: cs2 gear plan <target> …"));
        MainThreadReply.ReplyWhen<PlanPreview>(
            call,
            done => Engine.Search.SearchStart.Preview(request, done),
            (plan, error) =>
            {
                if (plan == null)
                {
                    call.Error(error?.Message ?? "the plan couldn't be made");
                    return;
                }
                call.Ok(GearPlanText.Of(plan));
            },
            TimeSpan.FromSeconds(MaxWaitSeconds),
            "the plan took too long"
        );
    }

    private static SearchRequest RequestFrom(CommandArgs args, string targetWord, SearchKind kind = SearchKind.BestGear)
    {
        var game = LiveGame.Require();
        var target = SearchTarget(targetWord);
        var setup = RunArgs.SetupFrom(args);
        setup.Changes = ChangeArgs.From(args, SideLetter.A).A;
        var request = new SearchRequest(
            kind,
            setup,
            target.Key,
            new Goal(args.String("metric") ?? Metrics.MainXp, DeathsFrom(args.String("deaths"))),
            args.Int("trials", 1000, 1, 100_000),
            args.Int("copies", RunArgs.DefaultCopies(), 1, Math.Max(1, Environment.ProcessorCount))
        )
        {
            Locks = Ids(args.String("lock")),
            Excluded = Ids(args.String("exclude")),
            AllStyles = StylesFrom(args.String("styles")),
            Seed = args.ULong("seed"),
            Where = RunArgs.Where(args),
            SlotAtATime = args.Flag("short"),
            Kinds = kind == SearchKind.BestGear ? KindsFrom(args.String("kinds")) : [],
            IncludeUnowned = kind == SearchKind.BestGear && args.Flag("unowned"),
        };
        return kind == SearchKind.BestGear ? PoolArgs.Apply(request, args) : request;
    }

    // The kinds besides gear: as given, all, none, or the switches saved in the panel.
    private static IReadOnlyList<ChoiceKind> KindsFrom(string? text)
    {
        if (text == null)
            return Panel.PanelState.Prefs.SearchKindList;
        if (text == "all")
            return ChoiceKinds.All;
        if (text == "none")
            return [];
        var kinds = new List<ChoiceKind>();
        foreach (var name in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            kinds.Add(
                ChoiceKinds.TryParse(name.ToLowerInvariant(), out var kind)
                    ? kind
                    : throw new CommandException(
                        $"no kind '{name}': use {string.Join(", ", ChoiceKinds.All.Select(ChoiceKinds.Name))}, all or none"
                    )
            );
        return ChoiceKinds.Normal(kinds);
    }

    // Pins the run shown doesn't have, which a search of the pins leaves out: "left out: …" and a new line, or "".
    private static string LeftOut(string word) =>
        word == "pinned" && Panel.PanelPins.WaitingPins is { Count: > 0 } waiting
            ? "left out: " + string.Join(", ", waiting.Select(Model.Ids.BriefKey)) + "\n"
            : "";

    // One target, a master, an area (area:<area>) or the pins of the run shown (pinned), two or three of them.
    private static TargetSpec SearchTarget(string word)
    {
        if (word != "pinned")
            return Targets.Resolve(LiveGame.Require(), word, sets: true);
        var pins = Panel.PanelPins.ShownPins;
        if (pins.Count < 2)
            throw new CommandException("pin two or three targets first");
        return TargetSpec.FromKey(Model.Ids.PinnedKey(pins));
    }

    // The search running now; else the kept answer for the target (the last found without one), with what has
    // changed since it was searched said first.
    private static void Show(CommandCall call, CommandArgs args, RunService service, SearchKind kind)
    {
        string Text(Answer answer) =>
            kind == SearchKind.Droptimizer ? GearText.OfDrops(answer, args.Flag("full")) : GearText.Of(answer);
        if (service.Search is { IsActive: true } search)
        {
            call.Ok(Running(search));
            return;
        }
        var game = LiveGame.Require();
        var target = args.Word(1) is { } word ? SearchTarget(word).Key : null;
        if (target == null && service.Search is { Answer: { Finished: false } ended } last && last.Request.Kind == kind)
        {
            call.Ok(Text(ended));
            return;
        }
        var setup = RunArgs.SetupFrom(args);
        setup.Changes = ChangeArgs.From(args, SideLetter.A).A;
        var answered = false;
        (Answer Answer, IReadOnlyList<Note> Changed)? found = null;
        Engine.Search.KeptAnswers.Find(
            kind,
            setup,
            target,
            RunService.DefaultTieShare(),
            result =>
            {
                found = result;
                answered = true;
            },
            kind == SearchKind.BestGear ? PoolArgs.Key(args) : null
        );
        MainThread.When(
            () => answered,
            () =>
            {
                if (found is not { } kept)
                    call.Error(
                        target == null
                            ? "no answer yet: search first"
                            : $"no answer kept for {Model.Ids.BriefKey(target)}"
                    );
                else if (kept.Changed.Count == 0)
                    call.Ok(Text(kept.Answer));
                else
                    call.Ok(
                        $"out of date: {string.Join(", ", kept.Changed.Select(n => n.English()))}\n{Text(kept.Answer)}"
                    );
            },
            TimeSpan.FromSeconds(MaxWaitSeconds),
            () => call.Error("the kept answer couldn't be checked in time")
        );
    }

    private static string Running(Engine.Search.SearchRun search) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"running: {(search.Stage == 0 ? "planning" : $"stage {search.Stage}, {search.Left} left")}{(search.SecondsLeft is { } s ? $", {(search.LeftIsLimit ? "at most" : "about")} {s:0} s to go" : "")} (cs2 wait, then cs2 gear show)"
        ) + BestSoFar();

    // A setup search's best confirmed set so far, which Stop keeps.
    private static string BestSoFar() =>
        RunService.Instance.BestSoFar() switch
        {
            null => "",
            { Changes.IsEmpty: true } => "\nbest so far: keep what you have",
            { Percent: { } percent } best => string.Create(
                CultureInfo.InvariantCulture,
                $"\nbest so far: {percent.Point * 100:+0.0;−0.0}% [{percent.Low * 100:0.0}, {percent.High * 100:0.0}] {best.Changes.ShortForm}"
            ),
            { } best => "\nbest so far: " + best.Changes.ShortForm,
        };

    private static int? Tie(CommandArgs args) => args.String("tie") == null ? null : args.Int("tie", 1, 1, 1000);

    internal static DeathLimit DeathsFrom(string? text) =>
        text?.ToLowerInvariant() switch
        {
            null or "none" => DeathLimit.None,
            "5" => DeathLimit.UnderFivePercent,
            "any" => DeathLimit.SurvivedOnce,
            var other => throw new CommandException($"deaths={other}: use deaths=none, deaths=5 or deaths=any"),
        };

    private static bool StylesFrom(string? text) =>
        text?.ToLowerInvariant() switch
        {
            null => false,
            "all" => true,
            var other => throw new CommandException($"styles={other}: use styles=all"),
        };

    // Ids separated by commas, short ones taken to be in the base namespace; whether each exists is the search's
    // to say, against the catalogue.
    private static HashSet<string> Ids(string? list) =>
        [
            .. (list ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Model.Ids.Full),
        ];
}
