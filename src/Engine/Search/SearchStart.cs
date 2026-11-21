using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>What starting a search reads from the live game, on the main thread: the snapshot and what names it.</summary>
internal static partial class SearchStart
{
    /// <summary>
    /// Reads the catalogue for <paramref name="search"/> from a check copy prepared with its base, plans it, and
    /// hands the plan to the search on the main thread.
    /// </summary>
    public static void Plan(SearchRun search)
    {
        var request = search.Request;
        Read(
            request,
            PlanWhat.Search,
            search.Snapshot,
            search.LoadoutSlot,
            search.Fighter,
            search.NewGeneration(),
            (reply, error) =>
            {
                if (!search.IsActive)
                    return;
                if (reply == null)
                {
                    error ??= new InvalidOperationException("no catalogue");
                    Log.Error("the search's catalogue couldn't be read", error);
                    search.PlanFailed(Note.Of("failed: {0}", error.Message));
                    return;
                }
                if (reply.Problem is { } problem)
                {
                    search.PlanFailed(problem);
                    return;
                }
                if (reply.Drops is { } drops)
                {
                    Log.Info(
                        $"droptimizer plan: {drops.Unowned} unowned, {drops.Wearable} wearable, {drops.Candidates.Count - 1} to fight"
                    );
                    search.PlannedDrops(drops, reply.Catalogue);
                    return;
                }
                var plan = reply.Plan!;
                Log.Info(
                    $"search plan: {plan.Possible} possible, {plan.Candidates.Count} to fight, {plan.LeftOut.Count} left out{(plan.Climb ? ", a slot at a time" : "")}"
                );
                search.Planned(plan, reply.Catalogue, reply);
            },
            search.Cancelled
        );
    }

    /// <summary>
    /// Reads a plan where the search fights: in the simulator host when it fights there, so the game builds no copy
    /// for it; else on the game's check copy. <paramref name="done"/> runs on the main thread.
    /// </summary>
    private static void Read(
        SearchRequest request,
        PlanWhat what,
        Snapshot snapshot,
        string? slot,
        ISearchFighter? fighter,
        int generation,
        Action<PlanReply?, Exception?> done,
        CancellationToken token
    )
    {
        var perTrial = PerTrial(request);
        if (
            fighter != null
            && fighter.Plan(new SearchPlanJob(snapshot, slot, request, what, perTrial, generation, token), done)
        )
            return;
        ChangeCheck.Read(
            snapshot,
            slot,
            request.Setup.Changes,
            request.Setup.IgnoreRequirements,
            copy => SearchPlanning.On(copy, request, what, perTrial, token),
            done
        );
    }

    /// <summary>
    /// A plan outside a search, read on the game's check copy: the host takes one job at a time on one link, and a
    /// form's counts or a kept answer's check can come while a run or another of them is under way.
    /// <paramref name="done"/> runs on the main thread.
    /// </summary>
    public static void ReadOnce(SearchRequest request, PlanWhat what, Action<PlanReply?, Exception?> done)
    {
        var game = LiveGame.Require();
        var slot = RunService.CheckLoadout(request.Setup, () => LiveGame.Loadouts(game));
        ReadOnce(request, what, LiveGame.TakeSnapshot(game), slot, done);
    }

    /// <summary>As the other, from a snapshot already taken.</summary>
    public static void ReadOnce(
        SearchRequest request,
        PlanWhat what,
        Snapshot snapshot,
        string? slot,
        Action<PlanReply?, Exception?> done
    )
    {
        request.Setup.Character = snapshot.Character;
        Read(request, what, snapshot, slot, null, 0, done, CancellationToken.None);
    }

    /// <summary>The seconds fighting every set of <paramref name="plan"/> is expected to take; null when not known.</summary>
    public static double? Expected(SearchPlan plan, SearchRequest request) =>
        SearchPlanning.Expected(plan, request, PerTrial(request));

    /// <summary>
    /// Plans <paramref name="request"/> without starting anything; <paramref name="done"/> runs on the main thread
    /// with the plan as every set would be fought, or the error.
    /// </summary>
    public static void PlanOnly(SearchRequest request, Action<SearchPlan?, Exception?> done) =>
        ReadOnce(
            request,
            PlanWhat.Counts,
            (reply, error) =>
            {
                if (reply == null)
                    done(null, error);
                else if (reply.Problem is { } problem)
                    done(null, new CommandException(problem.English()));
                else
                    done(reply.Plan, null);
            }
        );

    /// <summary>
    /// What the start form shows for <paramref name="request"/>: the plan's counts and how long it is expected to
    /// take, or for Droptimizer the unowned items and those it would fight; <paramref name="done"/> runs on the main
    /// thread.
    /// </summary>
    public static void Preview(SearchRequest request, Action<PlanPreview?, Exception?> done) =>
        ReadOnce(
            request,
            PlanWhat.Counts,
            (reply, error) =>
            {
                if (reply == null)
                    done(null, error);
                else if (reply.Problem is { } problem)
                    done(null, new CommandException(problem.English()));
                else
                    done(PreviewOf(reply, request), null);
            }
        );

    private static PlanPreview PreviewOf(PlanReply reply, SearchRequest request)
    {
        var perTrial = PerTrial(request);
        if (reply.Drops is { } drops)
        {
            var fight = Math.Max(0, drops.Candidates.Count - 1);
            return new PlanPreview
            {
                Kind = SearchKind.Droptimizer,
                Unowned = drops.Unowned,
                Wearable = drops.Wearable,
                RuledOut = drops.RuledOut,
                ToFight = fight,
                NothingToSearch = fight == 0,
                Seconds = perTrial is { } p
                    ? SearchTimes.Expected(p, drops.Candidates.Count, request.Trials, foughtOnce: true)
                    : null,
                LimitSeconds = SearchTimes.LongSearchSeconds,
            };
        }
        var plan = reply.Plan!;
        var limit = PlanPreview.LimitFor(request.Pool);
        var target = TargetSpec.FromKey(request.Target);
        var climbSeconds = SearchPlanning.ClimbExpected(plan, request, SearchTimes.StepPace(target.Key, target.Kind));
        var seconds = SearchPlanning.Expected(plan, request, perTrial);
        var all = KindCounts(reply, request);
        var kinds = all.Where(k => request.Kinds.Contains(k.Kind)).ToList();
        var kindSets = kinds.Sum(k => k.Sets);
        // Each kind's step races its options against the held set, about twice over.
        var kindSeconds =
            perTrial is { } each && kindSets > 0
                ? PlanPreview.KindPasses
                    * kinds.Where(k => k.Sets > 0).Sum(k => SearchTimes.Expected(each, k.Sets + 1, request.Trials))
                : (double?)null;
        return new PlanPreview
        {
            Kinds = all,
            On = request.Kinds,
            PotionsInBuild = LiveGame.Find() is { } game && Changes.PotionsInBuild(game),
            KindsOverLimit = (seconds ?? 0) + (kindSeconds ?? 0) > limit && kindSeconds != null,
            ClimbSeconds = climbSeconds is { } climbed ? climbed + (kindSeconds ?? 0) : null,
            Kind = SearchKind.BestGear,
            Possible = plan.Possible,
            RuledOut = plan.RuledOut.Values.Aggregate(System.Numerics.BigInteger.Zero, (n, r) => n + r),
            ToFight = plan.Candidates.Count,
            LeftOut = plan.LeftOut.Count,
            Climb = plan.Climb,
            NothingToSearch = plan.NothingToSearch && kindSets == 0,
            SkippedStyles = plan.SkippedStyles,
            Seconds = seconds is { } gear && kindSeconds is { } more ? gear + more : seconds,
            OverLimit = SearchPlanning.TooLong(plan, request, perTrial),
            LimitSeconds = limit,
            MaxSeconds = request.MaxSeconds,
            Pool = request.Pool,
            PoolCounts = request.Pool == ItemPool.Every ? PoolCount.Of(reply.Catalogue, request) : [],
        };
    }

    // What each kind switched on adds, with the setup's own weapon and stance.
    private static IReadOnlyList<KindCount> KindCounts(PlanReply reply, SearchRequest request)
    {
        var catalogue = reply.Catalogue;
        var weapon = catalogue.Items.FirstOrDefault(i => i.Worn != null && i.Weapon != null)?.Weapon;
        var magic = weapon != null && weapon.Style == "Magic";
        return [.. reply.Choices.Select(c => Choices.Count(c, weapon?.Elements ?? [], magic, request.IncludeUnowned))];
    }

    /// <summary>
    /// A search ready to begin: the snapshot taken now, the answer's key from the request, and where it fights.
    /// </summary>
    public static SearchRun New(SearchRequest request, double share, string where, ulong seed, Func<int> nextGeneration)
    {
        if (request.Refusals() is [var refused, ..])
            throw new CommandException(refused.English());
        RequireDependencies();
        var game = LiveGame.Require();
        var slot = RunService.CheckLoadout(request.Setup, () => LiveGame.Loadouts(game));
        return Begin(
            request,
            share,
            where,
            seed,
            nextGeneration,
            LiveGame.TakeSnapshot(game),
            slot,
            Fingerprints.SetupHash(game),
            LiveGame.TargetName(game, TargetSpec.FromKey(request.Target), masked: false),
            LiveGame.Prices(game)
        );
    }

    private static void RequireDependencies()
    {
        var game = LiveGame.Require();
        var deps = Dependencies.Resolve(game);
        var required = deps.Missing.Where(d => Dependencies.Required.Contains(d.Feature)).ToList();
        if (required.Count > 0)
            throw new CommandException("can't simulate on this game build, missing: " + string.Join(", ", required));
    }

    // The search on a snapshot taken: its answer's key, its fighter and its record. The game is read for names and
    // prices only.
    private static SearchRun Begin(
        SearchRequest request,
        double share,
        string where,
        ulong seed,
        Func<int> nextGeneration,
        Snapshot snapshot,
        string? slot,
        string setupHash,
        string targetName,
        PriceTable prices
    )
    {
        var target = TargetSpec.FromKey(request.Target);
        request.Setup.Character = snapshot.Character;
        var key = new AnswerKey
        {
            Character = snapshot.Character,
            Setup = request.Setup.Identity,
            GameBuild = snapshot.GameBuild,
            ModBuild = KeptAnswers.Build,
            Goal = request.Goal,
            Locks = [.. request.Locks.Order(StringComparer.Ordinal)],
            Excluded = [.. request.Excluded.Order(StringComparer.Ordinal)],
            AllStyles = request.AllStyles,
            Share = share,
            Kinds = [.. ChoiceKinds.Normal(request.Kinds).Select(ChoiceKinds.Name)],
            IncludeUnowned = request.IncludeUnowned,
        }.WithPool(request);
        ISearchFighter fighter =
            where == RunService.InHost
                ? (RunService.Remote ?? throw new InvalidOperationException("no simulator host")).NewSearch()
                : new LocalSearchFighter();
        fighter = new OwnLoadoutsFighter(fighter, nextGeneration);
        GuardLog.Reset();
        var id = RunIds.Next(DateTime.Now, [], "search-");
        Log.Info(
            $"search {id} of {target.Key} starts in {where}: seed {seed}, copies {request.Copies}, setup {request.Setup.Identity}"
        );
        return new SearchRun(request, snapshot, slot, target, targetName, seed, key, fighter, prices, nextGeneration)
        {
            Margin = share,
            Where = where,
            SetupHash = setupHash,
            Id = id,
            Record = SearchRecord.Of(
                id,
                request,
                share,
                seed,
                where,
                snapshot.Sha256,
                setupHash,
                snapshot.GameBuild,
                RunService.Cs2Mvid
            ),
        };
    }

    // The seconds a trial is expected to take against the request's target, from this session's fights.
    private static double? PerTrial(SearchRequest request)
    {
        var target = TargetSpec.FromKey(request.Target);
        return SearchTimes.PerTrial(target.Key, target.Kind);
    }
}
