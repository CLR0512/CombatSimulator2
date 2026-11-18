using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// Rankings, details and death rates of the shown run, kept until its results change. A run of every target has
/// thousands of estimates; the views ask for the same ones on every redraw. Main thread only.
/// </summary>
internal static class RunCache
{
    private static string _stamp = "";
    private static readonly Dictionary<(string Metric, TimeUnit Unit), RankTable> _ranks = [];
    private static readonly Dictionary<(string Key, TimeUnit Unit), IReadOnlyList<DetailRow>> _details = [];
    private static readonly Dictionary<string, double> _deaths = new(StringComparer.Ordinal);
    private static readonly Dictionary<(string Key, string Metric), Model.Slayer.BlockSuggestion?> _blocks = [];
    private static List<Metric>? _metrics;

    /// <summary>
    /// What identifies the run's results as shown: its id, state and how many targets have a result, and the language
    /// (labels and names are cached in it).
    /// </summary>
    public static string Stamp(Run? run) =>
        run == null
            ? ""
            : $"{run.Id}/{run.State}/{run.Done}/{run.Results.Values.Count(r => r.State is TargetState.Building or TargetState.Fighting)}/{Tr.Version}";

    private static void Check(Run run)
    {
        var stamp = Stamp(run);
        if (stamp == _stamp)
            return;
        _stamp = stamp;
        _ranks.Clear();
        _details.Clear();
        _deaths.Clear();
        _blocks.Clear();
        _metrics = null;
    }

    /// <summary>The metrics "Rank by" offers for the run.</summary>
    public static List<Metric> Metrics(Run? run)
    {
        if (run == null)
            return MetricsOf(null);
        Check(run);
        return _metrics ??= MetricsOf(run);
    }

    /// <summary>Computes what the next redraw needs, so it can happen on a frame of its own.</summary>
    /// <param name="step">0: the metric list; 1: the ranking by the chosen metric.</param>
    public static void Warm(Run? run, int step)
    {
        if (run == null)
            return;
        if (step == 0)
            Metrics(run);
        else
            Rank(run, PanelState.Prefs.Metric, PanelState.Prefs.Unit);
    }

    public static RankTable Rank(Run run, string metric, TimeUnit unit)
    {
        Check(run);
        if (!_ranks.TryGetValue((metric, unit), out var table))
            _ranks[(metric, unit)] = table = Ranking.Rank(run, metric, unit, PanelState.Prices());
        return table;
    }

    public static IReadOnlyList<DetailRow> Details(Run run, string key, TimeUnit unit)
    {
        Check(run);
        if (!_details.TryGetValue((key, unit), out var rows))
            _details[(key, unit)] = rows = Ranking.Details(
                run,
                key,
                unit,
                PanelState.Prices(),
                PanelState.Names(),
                skipTargetsZeros: true
            );
        return rows;
    }

    /// <summary>A master's block suggestion by <paramref name="metric"/>; null for other targets and without figures.</summary>
    public static Model.Slayer.BlockSuggestion? Blocks(Run run, string key, string metric)
    {
        Check(run);
        if (!_blocks.TryGetValue((key, metric), out var suggestion))
            _blocks[(key, metric)] = suggestion = run.Results.GetValueOrDefault(key)
                is { Kind: TargetKind.Master } master
                ? Model.Slayer.BlockSuggestions.For(
                    master,
                    Model.Metrics.Resolve(run, metric),
                    TimeUnit.Hour,
                    PanelState.Prices(),
                    Ranking.LowerIsBetter(Model.Metrics.Resolve(run, metric))
                )
                : null;
        return suggestion;
    }

    public static double DeathRate(Run run, string key)
    {
        Check(run);
        if (!_deaths.TryGetValue(key, out var rate))
            _deaths[key] = rate = run.Results.GetValueOrDefault(key) is { } result
                ? Model.Metrics.Estimate(result, Model.Metrics.DeathRate, TimeUnit.Hour)?.Point ?? 0
                : 0;
        return rate;
    }

    public static void Clear()
    {
        _stamp = "";
        _ranks.Clear();
        _details.Clear();
        _deaths.Clear();
        _blocks.Clear();
        _metrics = null;
    }

    /// <summary>
    /// The metrics a run offers: "XP · main skill" first, then every metric present, with XP in every combat skill: a
    /// master's tasks can train any of them through their loadouts.
    /// </summary>
    public static List<Metric> MetricsOf(Run? run) => run == null ? MetricsOf() : MetricsOf(run, run);

    /// <summary>
    /// The metrics either of two compared runs offers, in one order: a metric only one side has sits where it belongs
    /// rather than after the other side's.
    /// </summary>
    public static List<Metric> MetricsOf(Run a, Run b)
    {
        var list = MetricsOf();
        var results = ReferenceEquals(a, b) ? a.Results.Values : a.Results.Values.Concat(b.Results.Values);
        list.AddRange(Model.Metrics.For(results, PanelState.Prices(), PanelState.Names(), Game.Changes.CombatSkills));
        return list;
    }

    private static List<Metric> MetricsOf() => [new(Model.Metrics.MainXp, Note.Of("XP · main skill"), true)];
}
