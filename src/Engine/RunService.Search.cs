using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine.Search;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine;

// The one door to a search: the panel and `cs2` start, watch and cancel it here, as they do runs.
internal sealed partial class RunService
{
    /// <summary>The search on screen: the active one, or the last one.</summary>
    public SearchRun? Search { get; private set; }

    /// <summary>The margin searches tie by when a request doesn't say: the player's setting.</summary>
    public static Func<double> DefaultTieShare { get; set; } = () => Preferences.DefaultTieShare;

    /// <summary>
    /// Starts a search for <paramref name="request"/>: the snapshot is taken now, on the main thread; the catalogue
    /// is read from a check copy and the plan made off it; then the stages are fought on their own threads or in the
    /// simulator host. With <paramref name="candidates"/> (current gear first), those sets are fought and nothing
    /// is planned. One search or run at a time.
    /// </summary>
    public SearchRun StartSearch(SearchRequest request, IReadOnlyList<Candidate>? candidates = null)
    {
        MainThread.Assert("RunService.StartSearch");
        if (IsStarting)
            throw new CommandException("a run is starting: wait for it");
        if (Current is { IsFinal: false } || _pendingB != null)
            throw new CommandException($"{Current!.Id} is still running: cancel it first (cs2 cancel)");
        if (Search is { IsActive: true })
            throw new CommandException("a gear search is still running: cancel it first (cs2 gear cancel)");
        if (BestLoadouts is { IsActive: true })
            throw new CommandException("Best loadouts is running: cancel it first (cs2 loadouts best cancel)");
        if (candidates == null && !Game.Dependencies.IsAvailable(Game.Dependencies.GearSearch))
            throw new CommandException("the gear search is unsupported on this game build (see cs2 deps)");
        if (Ids.IsMaster(request.Target) && !Game.Dependencies.IsAvailable(Game.Dependencies.SlayerMasters))
            throw new CommandException("Slayer masters are unsupported on this game build (see cs2 deps)");
        if (Ids.IsMaster(request.Target) && EveryMonsterOwnLoadout(request))
            throw new CommandException("Every monster has its own loadout: nothing to search");
        var search = SearchStart.New(
            request,
            DefaultTieShare(),
            request.Where ?? DefaultWhere(),
            request.Seed ?? NewSeed(),
            () => ++_generation
        );
        search.Changed += () => _changed = true;
        search.Ended += KeptAnswers.Keep;
        LastSearches[request.Kind] = search;
        Search?.Dispose();
        Search = search;
        _changed = true;
        if (candidates == null)
            SearchStart.Plan(search);
        else
            search.Begin(candidates);
        return search;
    }

    // A master whose every monster wears a loadout of its own leaves no setup to search.
    private static bool EveryMonsterOwnLoadout(SearchRequest request)
    {
        var master = Ids.MasterOf(request.Target);
        if (request.Setup.Changes?.Find(new ChangeKey(ChangeKind.Loadouts, master)) is not { Loadouts.Count: > 0 })
            return false;
        var pool = Game.Slayer.SlayerTasks.Read(Game.LiveGame.Require(), master, request.Setup.Changes)?.Pool ?? [];
        return pool.Count > 0
            && Model.Slayer.PartSetups.Of(request.Setup.Changes, pool.Select(p => p.Key)).Count == pool.Count;
    }

    /// <summary>
    /// Keeps a set of the last search's answer as a saved run like any other: its result of the last stage, with
    /// nothing fought. <paramref name="tie"/> picks a set as good as the best, from 1.
    /// </summary>
    public Run KeepAnswer(int? tie = null)
    {
        MainThread.Assert("RunService.KeepAnswer");
        var (search, set) = AnswerSet(tie);
        var run = AnswerRuns.Of(search, set, RunIds.Next(DateTime.Now, KnownIds().Append(Current?.Id ?? "")));
        Current = run;
        _side = null;
        _changed = true;
        Finalised?.Invoke(run);
        return run;
    }

    /// <summary>
    /// Opens a set of the last search's answer as a what-if against current gear: side A is the search's base, side
    /// B the base with the set's changes, started as any comparison with the search's seed. Both results are put
    /// where a run looks for earlier ones first, so the sides are answered without a fight where they match.
    /// </summary>
    /// <summary>
    /// A setup search under way: the best set a stop now would keep, with its gain over current gear; its changes
    /// are empty while current gear leads. Null for a gear search, or before its first step ends.
    /// </summary>
    public (Estimate? Percent, ChangeList Changes)? BestSoFar() =>
        Search is not { IsActive: true, Request.Kinds.Count: > 0, FinishedSoFar: { } done } search ? null
        : SearchProgress.BestSoFar(done.Results, search.Request.Goal, search.Margin) is { } best
            ? (best.Percent, search.Candidates[best.Index].Changes)
        : (null, ChangeList.Empty);

    public void OpenAnswer(
        int? tie,
        Action<SidesStart> started,
        Action<Exception> failed,
        string? item = null,
        string? alternative = null
    )
    {
        MainThread.Assert("RunService.OpenAnswer");
        var (search, set) = AnswerSet(tie, item, alternative);
        var request = AnswerRuns.Comparison(search, set, KnownIds().Append(Current?.Id ?? ""), Results);
        StartSides(request, AnswerRuns.SetupOf(search, set), started, failed);
    }

    /// <summary>
    /// A set of <paramref name="answer"/> as found, with the changes it makes to the setup it was searched from: the
    /// best, a tie from 1, or one of Droptimizer's items. Nothing is fought; the panel puts them on side B.
    /// </summary>
    public static SetResult AnswerSetOf(Answer answer, int? tie, string? item = null, string? alternative = null)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return AnswerRuns.Pick(answer, tie, item, alternative);
    }

    private (SearchRun Search, SetResult Set) AnswerSet(int? tie, string? item = null, string? alternative = null)
    {
        if (IsActive)
            throw new CommandException("a run or search is still going: wait for it or cancel it first");
        if (Search is not { Answer: { Finished: true } answer } search)
            throw new CommandException("no answer from this session: search first (cs2 gear <target>)");
        return (search, AnswerRuns.Pick(answer, tie, item, alternative));
    }

    /// <summary>
    /// What <paramref name="request"/> would fight, read from a check copy without starting anything;
    /// <paramref name="done"/> runs on the main thread with the counts or the error.
    /// </summary>
    public static void PreviewSearch(SearchRequest request, Action<PlanPreview?, Exception?> done)
    {
        MainThread.Assert("RunService.PreviewSearch");
        SearchStart.Preview(request, done);
    }

    /// <summary>
    /// The kept answer of <paramref name="kind"/> for <paramref name="target"/>, with what has changed since it was
    /// searched against <paramref name="setup"/> as it stands; <paramref name="done"/> runs on the main thread, with
    /// null when none is kept.
    /// </summary>
    public static void FindKept(
        SearchKind kind,
        Setup setup,
        string target,
        Action<(Answer Answer, IReadOnlyList<Note> Changed)?> done
    )
    {
        MainThread.Assert("RunService.FindKept");
        KeptAnswers.Find(kind, setup, target, DefaultTieShare(), done);
    }

    /// <summary>How many search answers are kept, to show again on returning to their target.</summary>
    public static int KeptAnswerCount => KeptAnswers.Store?.Count ?? 0;

    /// <summary>Forgets every kept search answer; saved runs stay.</summary>
    public static void ClearKeptAnswers()
    {
        MainThread.Assert("RunService.ClearKeptAnswers");
        KeptAnswers.Store?.Clear();
    }

    /// <summary>Cancels the active search; saved runs, pins and the comparison are as they were.</summary>
    public SearchRun CancelSearch()
    {
        MainThread.Assert("RunService.CancelSearch");
        var search = Search is { IsActive: true } active ? active : throw new CommandException("no search is active");
        search.Cancel();
        return search;
    }
}
