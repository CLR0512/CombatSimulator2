using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

internal static partial class SearchDev
{
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
                PoolArgs.Key(args) with
                {
                    Character = snapshot.Character,
                }
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
                var request = PoolArgs.Apply(
                    new SearchRequest(SearchKind.BestGear, setup, target.Key, goal, trials, copies)
                    {
                        Locks = catalogue
                            .Slots.Select(s => s.Id)
                            .Where(s => !free.Contains(s))
                            .ToHashSet(StringComparer.Ordinal),
                    },
                    args
                );
                if (request.Check(catalogue) is [var problem, ..])
                {
                    _exhausted = "refused: " + problem.English();
                    return;
                }
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
}
