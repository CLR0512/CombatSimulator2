using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// <c>cs2dev search</c>: the catalogue and plan of a search without a fight, and given sets fought through the
/// search's own pool and stages, with each set's result hash to hold beside a run of the same changes.
/// </summary>
internal static class SearchDev
{
    public static Subcommand Sub { get; } =
        new(
            "search",
            "cs2dev search plan <target> [metric=] [styles=all] [setup=<loadout>] [<change>…] | cs2dev search planned | cs2dev search fight <target> <change…> [| <change…>]… [trials=] [seed=] [copies=] [where=] [metric=] | cs2dev search state | cs2dev search unknown <item>|off | cs2dev search limit <seconds>|off | cs2dev search keys <target> <change…> [| <change…>]… | cs2dev search keys <target> plan [lock=] [metric=] [<change>…] | cs2dev search keys result | cs2dev search exhaust <target> slots=<a,b,c> [trials=5000] [metric=] | cs2dev search exhausted | cs2dev search census <target> | cs2dev search censused | cs2dev search ladder <target> [a.<change>…] <change…> [| <change…>]… [counts=100,400,1000] [truth=] [truthseed=] [seed=] [metric=] | cs2dev search laddered | cs2dev search arms [trials=1000] [truth=] [checks=] [widen=] | cs2dev search prefix | cs2dev search batches <count>",
            [
                "trials",
                "setup",
                "seed",
                "copies",
                "where",
                "metric",
                "styles",
                "slots",
                "lock",
                "counts",
                "truth",
                "checks",
                "truthseed",
                "widen",
                .. ChangeArgs.Options,
            ],
            ["ignore-req"],
            Run,
            Purpose: "plan: the catalogue and what a search would fight, without fighting; fight: given sets through the search's own stages, each with its result hash"
        );

    private static void Run(CommandCall call, CommandArgs args)
    {
        switch (args.Word(0))
        {
            case "plan":
                Plan(call, args);
                break;
            case "planned":
                call.Ok(_planned);
                break;
            case "fight":
                Fight(call, args);
                break;
            case "keys" when args.Word(1) == "result":
                call.Ok(SearchKeysDev.Result);
                break;
            case "keys" when args.Word(2) == "plan":
                SearchKeysDev.OfPlan(call, args);
                break;
            case "keys":
                SearchKeysDev.OfSets(call, args);
                break;
            case "leak":
                Leak(call, args);
                break;
            case "state":
                State(call);
                break;
            case "exhaust":
                Exhaust(call, args);
                break;
            case "exhausted":
                call.Ok(_exhausted);
                break;
            case "ladder" or "laddered" or "arms" or "prefix" or "batches" or "key":
                SearchLadderDev.Run(call, args);
                break;
            case "census":
                SearchCensusDev.Start(call, args);
                break;
            case "censused":
                call.Ok(SearchCensusDev.Result);
                break;
            case "limit":
                var limit = args.Word(1) ?? throw new CommandException("usage: cs2dev search limit <seconds>|off");
                Engine.Search.SearchTimes.LimitSeconds =
                    limit == "off"
                        ? Engine.Search.SearchTimes.LongSearchSeconds
                        : double.Parse(limit, CultureInfo.InvariantCulture);
                call.Ok(
                    $"a search fighting every set goes a slot at a time after {Engine.Search.SearchTimes.LimitSeconds.ToString(CultureInfo.InvariantCulture)} s of its first stage"
                );
                break;
            case "unknown":
                var item = args.Word(1) ?? throw new CommandException("usage: cs2dev search unknown <item>|off");
                Game.Search.KnownParts.MakeUnknown(item == "off" ? null : Ids.Full(item));
                call.Ok(
                    item == "off"
                        ? "every item is read as it is"
                        : $"{Ids.Full(item)} is read as carrying a part the search doesn't know"
                );
                break;
            default:
                throw new CommandException("usage: " + Sub.Usage);
        }
    }

    /// <summary>
    /// Starts a search of the sets given as change lists separated by <c>|</c>, current gear first, and replies at
    /// once; <see cref="State"/> prints how it stands.
    /// </summary>
    private static void Fight(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var target = Targets.Resolve(
            game,
            args.Word(1) ?? throw new CommandException("usage: cs2dev search fight <target> <change…> [| <change…>]…")
        );
        var (setup, candidates) = SearchLadderDev.SetsFrom(args);
        var request = new SearchRequest(
            SearchKind.BestGear,
            setup,
            target.Key,
            new Goal(args.String("metric") ?? Metrics.MainXp),
            args.Int("trials", 1000, 1, 100_000),
            args.Int("copies", RunArgs.DefaultCopies(), 1, Math.Max(1, Environment.ProcessorCount))
        )
        {
            Seed = args.ULong("seed") ?? 1,
            Where = RunArgs.Where(args),
        };
        var search = RunService.Instance.StartSearch(request, candidates);
        call.Ok(
            $"search of {Ids.BriefKey(target.Key)} started: {candidates.Count} sets, seed {search.Seed} (cs2dev search state)"
        );
    }

    // What a set leaves on a copy once undone, read as the stats key: cs2dev search leak <target> <change…>.
    private static void Leak(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var target = Targets.Resolve(
            game,
            args.Word(1) ?? throw new CommandException("usage: cs2dev search leak <target> <change…>")
        );
        var changes = ChangeArgs.From(args, SideLetter.A).A ?? ChangeList.Empty;
        var setup = RunArgs.SetupFrom(args);
        var slot = RunService.CheckLoadout(setup, () => LiveGame.Loadouts(game));
        MainThreadReply.ReplyWhen<string>(
            call,
            done =>
                ChangeCheck.Read(
                    LiveGame.TakeSnapshot(game),
                    slot,
                    null,
                    false,
                    copy => Game.Search.TargetStatsKey.Leak(copy, target, changes, false),
                    done
                ),
            (text, error) =>
            {
                if (text == null)
                    call.Error(error?.Message ?? "no answer");
                else
                    call.Ok(text);
            },
            TimeSpan.FromSeconds(9),
            "the probe took too long"
        );
    }

    private static volatile string _planned = "no plan yet (cs2dev search plan <target>)";

    /// <summary>
    /// Reads the catalogue for the current setup and plans a search without fighting; <c>cs2dev search planned</c> prints
    /// it: the slots, what is owned, wearable and opaque (with why), and the counts with and without comparing.
    /// </summary>
    private static void Plan(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var target = Targets.Resolve(
            game,
            args.Word(1) ?? throw new CommandException("usage: cs2dev search plan <target>")
        );
        var setup = RunArgs.SetupFrom(args);
        var slot = RunService.CheckLoadout(setup, () => LiveGame.Loadouts(game));
        var changes = ChangeArgs.From(args, SideLetter.A).A;
        var request = new SearchRequest(
            SearchKind.BestGear,
            setup,
            target.Key,
            new Goal(args.String("metric") ?? Metrics.MainXp),
            1000,
            4
        )
        {
            AllStyles = args.String("styles") == "all",
        };
        _planned = "planning";
        ChangeCheck.Read(
            LiveGame.TakeSnapshot(game),
            slot,
            changes,
            setup.IgnoreRequirements,
            copy => Game.Search.GearCatalog.Read(copy, target),
            (catalogue, error) =>
            {
                if (catalogue == null)
                {
                    _planned = $"error {error?.GetType().Name}: {error?.Message}";
                    return;
                }
                var text = new StringBuilder(
                    $"catalogue {catalogue.Hash}: {catalogue.Items.Count} items, stance {Ids.Brief(catalogue.Stance)}"
                );
                foreach (var gearSlot in catalogue.Slots)
                {
                    var fits = catalogue.Items.Where(i => i.SlotType == gearSlot.Type).ToList();
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\n{Ids.Brief(gearSlot.Id), -10} {(gearSlot.Unlocked ? "" : "locked "), -7}worn {Ids.Brief(catalogue.Current.GetValueOrDefault(gearSlot.Id) ?? "-"), -22} fits {fits.Count, 3}, owned {fits.Count(i => i.Owned > 0), 3}, wearable {fits.Count(i => i.Owned > 0 && i.Wearable), 3}, opaque {fits.Count(i => i.Owned > 0 && i.Opaque), 2}, with effects {fits.Count(i => i.Owned > 0 && i.Effects.Count > 0), 2}, attacks {fits.Count(i => i.Owned > 0 && i.Attacks.Count > 0), 2}"
                    );
                }
                foreach (
                    var reason in catalogue
                        .Items.Where(i => i.Opaque)
                        .GroupBy(i => i.OpaqueReason)
                        .OrderByDescending(g => g.Count())
                )
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\nopaque, {reason.Key}: {reason.Count()} ({reason.Count(i => i.Owned > 0)} owned): {string.Join(", ", reason.Take(6).Select(i => Ids.Brief(i.Id)))}"
                    );
                for (var pass = 0; pass < 2; pass++)
                {
                    var comparing = pass == 1;
                    var plan = Planner.Plan(catalogue, request, comparing);
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\n{(comparing ? "comparing" : "not comparing")}: {plan.Branches.Count} branches, {plan.Possible} possible, ruled out {string.Join(", ", plan.RuledOut.Select(r => $"{r.Key} {r.Value}"))}, {(plan.Climb ? "too many to list" : $"{plan.Candidates.Count} to fight")}, {plan.LeftOut.Count} left out"
                    );
                    // Every branch with its style: the probes' climb picks each step's options from them.
                    foreach (var branch in comparing ? plan.Branches.Take(60) : [])
                    {
                        text.Append(
                            CultureInfo.InvariantCulture,
                            $"\n  branch {Ids.Brief(branch.Weapon ?? "-")} {Ids.Brief(branch.Stance)} {branch.Style}: {branch.Count} sets"
                        );
                        foreach (var unit in branch.Units)
                            text.Append(
                                CultureInfo.InvariantCulture,
                                $"\n    {string.Join('+', unit.Slots.Select(Ids.Brief))}: {string.Join(", ", unit.Pool.Select(Ids.Brief))}{(unit.AllowEmpty ? ", nothing" : "")}"
                            );
                    }
                    if (comparing)
                        foreach (var candidate in plan.Candidates.Take(12))
                            text.Append(
                                CultureInfo.InvariantCulture,
                                $"\n  {candidate.Index}: {(candidate.IsCurrent ? "current gear" : candidate.Changes.ShortForm)}"
                            );
                    foreach (var left in plan.LeftOut.Take(comparing ? 0 : 8))
                        text.Append(CultureInfo.InvariantCulture, $"\n  left out: {left.What}: {left.Why.English()}");
                }
                _planned = text.ToString();
            }
        );
        call.Ok("planning (cs2dev search planned)");
    }

    private static volatile string _exhausted = "nothing exhausted yet (cs2dev search exhaust <target> slots=<a,b,c>)";

    /// <summary>
    /// The ground truth for a small case: every combination of the named slots (the others kept as worn) fought in
    /// full from seed 1, then the best and the sets as good among them, and whether the kept answer for the target
    /// is the best or one of them. <c>cs2dev search exhausted</c> prints it.
    /// </summary>
    private static void Exhaust(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var target = Targets.Resolve(game, args.Word(1) ?? throw new CommandException("usage: " + Sub.Usage));
        var free = (args.String("slots") ?? throw new CommandException("slots=<a,b,c>: the slots to try every item in"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Ids.Full)
            .ToHashSet(StringComparer.Ordinal);
        var setup = RunArgs.SetupFrom(args);
        var slot = RunService.CheckLoadout(setup, () => LiveGame.Loadouts(game));
        var goal = new Goal(args.String("metric") ?? Metrics.MainXp);
        var trials = args.Int("trials", 5000, 1, 100_000);
        var copies = args.Int("copies", RunArgs.DefaultCopies(), 1, Math.Max(1, Environment.ProcessorCount));
        var snapshot = LiveGame.TakeSnapshot(game);
        var prices = LiveGame.Prices(game);
        var share = RunService.DefaultTieShare();
        var kept = Engine
            .Search.KeptAnswers.Store?.Find(
                SearchKind.BestGear,
                target.Key,
                new AnswerKey { Character = snapshot.Character }
            )
            ?.Answer;
        _exhausted = "reading the catalogue";
        ChangeCheck.Read(
            snapshot,
            slot,
            setup.Changes,
            setup.IgnoreRequirements,
            copy => Game.Search.GearCatalog.Read(copy, target),
            (catalogue, error) =>
            {
                if (catalogue == null)
                {
                    _exhausted = $"error {error?.GetType().Name}: {error?.Message}";
                    return;
                }
                var request = new SearchRequest(SearchKind.BestGear, setup, target.Key, goal, trials, copies)
                {
                    Locks = catalogue
                        .Slots.Select(s => s.Id)
                        .Where(s => !free.Contains(s))
                        .ToHashSet(StringComparer.Ordinal),
                };
                var plan = Planner.Plan(catalogue, request, comparing: false);
                if (plan.Climb)
                {
                    _exhausted = $"too many sets to fight them all: {plan.Possible}";
                    return;
                }
                _exhausted = $"fighting {plan.Candidates.Count} sets, {trials} trials each";
                var results = new System.Collections.Concurrent.ConcurrentDictionary<int, StageResult>();
                var pool = new Engine.Search.SearchPool();
                pool.Start(
                    new Engine.Search.SearchJob(
                        snapshot,
                        slot,
                        setup.Changes,
                        target,
                        new DriveOptions(trials, new TrialLimits(), setup.IgnoreRequirements),
                        1,
                        copies,
                        plan.Candidates
                    ),
                    new Exhausting(
                        (index, result, skill) =>
                            results[index] = new StageResult(index, result, Answers.MetricFor(goal, skill)),
                        () =>
                        {
                            pool.Dispose();
                            _exhausted = Exhausted(
                                plan,
                                [.. results.Values.OrderBy(r => r.Index)],
                                goal,
                                share,
                                prices,
                                kept
                            );
                        },
                        e =>
                        {
                            pool.Dispose();
                            _exhausted = $"error {e.GetType().Name}: {e.Message}";
                        }
                    ),
                    CancellationToken.None
                );
            }
        );
        call.Ok("exhausting (cs2dev search exhausted)");
    }

    internal sealed class Exhausting(Action<int, TargetResult, string?> done, Action finished, Action<Exception> failed)
        : Engine.Search.ISearchReport
    {
        public void CandidateDone(int index, TargetResult result, Note? left, string? mainSkill) =>
            done(index, result, mainSkill);

        public void Finished(PoolStats stats) => finished();

        public void Failed(Exception error) => failed(error);
    }

    // The exhaustive best and its ties, and where the kept answer stands among them.
    private static string Exhausted(
        SearchPlan plan,
        List<StageResult> results,
        Goal goal,
        double share,
        PriceTable prices,
        Answer? kept
    )
    {
        var end = Race.End(results, goal, share, prices);
        var text = new StringBuilder($"exhausted {results.Count} sets: {end.State}, margin {share * 100:0.#}%");
        string Line(int index) => index == 0 ? "current gear" : plan.Candidates[index].Changes.ShortForm;
        string Value(int index) =>
            Race.Value(results.First(r => r.Index == index), prices) is { } v
                ? v.Point.ToString("#,0.##", CultureInfo.InvariantCulture)
                : "-";
        var group = new List<int>();
        if (end.Best is { } best)
        {
            group.Add(best);
            group.AddRange(end.Ties.Select(t => t.Index));
            text.Append(CultureInfo.InvariantCulture, $"\nbest: {Line(best)}  {Value(best)}");
            foreach (var tie in end.Ties)
                text.Append(CultureInfo.InvariantCulture, $"\n  as good: {Line(tie.Index)}  {Value(tie.Index)}");
        }
        if (kept?.Best is not { } answer)
        {
            text.Append("\nkept answer: none with a best set");
            return text.ToString();
        }
        var at = plan.Candidates.FirstOrDefault(c => c.Changes.Equals(answer.Changes))?.Index;
        text.Append("\nkept answer: ").Append(answer.Changes.ShortForm).Append(": ");
        text.Append(
            at == null ? "not among the sets exhausted (another slot changed)"
            : group.Contains(at.Value) ? (at == end.Best ? "the exhaustive best" : "one of the exhaustive best's ties")
            : $"NOT the best or a tie ({Value(at.Value)})"
        );
        return text.ToString();
    }

    /// <summary>The search as it stands: its stages, then every set with its last result's state, trials and hash.</summary>
    private static void State(CommandCall call)
    {
        if (RunService.Instance.Search is not { } search)
            throw new CommandException("no search yet");
        var text = new StringBuilder(
            search.Answer is { } answer
                ? $"ended: {answer.State}"
                : $"running: stage {search.Stage}, {search.Left} left"
        );
        foreach (var (stage, i) in search.Stages.Select((s, i) => (s, i)))
            text.Append(
                CultureInfo.InvariantCulture,
                $"\nstage {i + 1}: {stage.Trials} trials, {stage.Fought} fought, {stage.Dropped} dropped, {stage.Left} left, {stage.Seconds:0.00} s"
            );
        foreach (var candidate in search.Candidates)
        {
            var changes = candidate.IsCurrent ? "current gear" : candidate.Changes.ShortForm;
            if (!search.Results.TryGetValue(candidate.Index, out var set))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n{candidate.Index}: {changes}: not fought yet");
                continue;
            }
            var value = Race.Value(set);
            text.Append(
                CultureInfo.InvariantCulture,
                $"\n{candidate.Index}: {changes}: {ResultText.Name(set.Result.State)}, {set.Result.TrialsDone} trials, {set.Metric} {(value == null ? "-" : value.Point.ToString("0.####", CultureInfo.InvariantCulture))}, deaths {set.Result.Batches.Sum(b => b.Deaths)}, game s {set.Result.GameSeconds:0}, hash {ResultsHashInput.Brief([set.Result])}"
            );
            if (set.Result.Reasons.Count > 0)
                text.Append(": ").AppendJoin("; ", set.Result.Reasons.Select(r => r.English()));
        }
        text.Append(
            CultureInfo.InvariantCulture,
            $"\nfights {search.Stages.Sum(st => (long)st.Fought * st.Trials)} trials, {search.Stages.Sum(st => st.Seconds):0.00} s"
        );
        if (search.Answer is { } done)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nanswer {done.State} best: {AnswerLine(done.Best)}");
            foreach (var tie in done.Ties)
                text.Append(CultureInfo.InvariantCulture, $"\ntie: {AnswerLine(tie)}");
            if (done.Closest != null)
                text.Append(CultureInfo.InvariantCulture, $"\nclosest: {AnswerLine(done.Closest)}");
            if (done.Reason != null)
                text.Append(CultureInfo.InvariantCulture, $"\nreason: {done.Reason.English()}");
        }
        if (search.Answer is { LeftOut.Count: > 0 } ended)
            foreach (var left in ended.LeftOut)
                text.Append(CultureInfo.InvariantCulture, $"\nleft out: {left.What}: {left.Why.English()}");
        call.Ok(text.ToString());
    }

    private static string AnswerLine(SetResult? set) =>
        set == null
            ? "-"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{(set.Changes.IsEmpty ? "held" : set.Changes.ShortForm)} gain {Estimated(set.Gain)} pct {Estimated(set.Percent)} deaths {set.Result.Batches.Sum(b => b.Deaths)}/{set.Result.Batches.Sum(b => b.Trials)}"
            );

    private static string Estimated(Estimate? e) =>
        e == null
            ? "-"
            : string.Create(CultureInfo.InvariantCulture, $"{e.Point:0.####} [{e.Low:0.####},{e.High:0.####}]");
}
