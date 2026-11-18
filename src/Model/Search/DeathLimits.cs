using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>The death limit judged from a set's trials: when to drop it during a search, and whether it may be the
/// answer at the end.</summary>
public static class DeathLimits
{
    /// <summary>The share of trials that may die under <see cref="DeathLimit.UnderFivePercent"/>.</summary>
    public const double Share = 0.05;

    /// <summary>
    /// Whether <paramref name="result"/> already shows the set can't meet the limit, so it is dropped: a death at
    /// all; a death share whose 95% range lies at or above 5%; or every trial died.
    /// </summary>
    public static bool Over(DeathLimit limit, TargetResult result)
    {
        var (deaths, trials) = Counts(result);
        return limit switch
        {
            DeathLimit.None => deaths > 0,
            DeathLimit.UnderFivePercent => trials > 0 && Estimators.Wilson(deaths, trials).Low >= Share,
            _ => trials > 0 && deaths >= trials,
        };
    }

    /// <summary>Whether the set, at the search's end, is inside the limit: only such a set is the answer or a tie.</summary>
    public static bool Within(DeathLimit limit, TargetResult result)
    {
        var (deaths, trials) = Counts(result);
        if (trials == 0)
            return false;
        return limit switch
        {
            DeathLimit.None => deaths == 0,
            DeathLimit.UnderFivePercent => deaths < Share * trials,
            _ => deaths < trials,
        };
    }

    /// <summary>
    /// The set that came closest when none is within the limit: the smallest share of deaths, the earlier set on
    /// equal shares. Null without any trials.
    /// </summary>
    public static int? Closest(IEnumerable<(int Index, TargetResult Result)> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        return sets.Select(s => (s.Index, Counts: Counts(s.Result)))
            .Where(s => s.Counts.Trials > 0)
            .OrderBy(s => s.Counts.Deaths / (double)s.Counts.Trials)
            .ThenBy(s => s.Index)
            .Select(s => (int?)s.Index)
            .FirstOrDefault();
    }

    /// <summary>The deaths and trials of a result's batches.</summary>
    public static (int Deaths, int Trials) Counts(TargetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var batches = Trials(result).ToList();
        return (batches.Sum(b => b.Deaths), batches.Sum(b => b.Trials));
    }

    // A master's trials are its monsters', a set's its targets'.
    private static IEnumerable<Tally> Trials(TargetResult result) =>
        result.Kind switch
        {
            TargetKind.Master => MasterParts.Counted(result).SelectMany(p => p.Batches),
            TargetKind.Set => (result.Parts ?? []).SelectMany(Trials),
            _ => result.Batches,
        };

    /// <summary>
    /// The set of <paramref name="round"/> that died least by its last result in <paramref name="last"/>, among
    /// those with a value; <paramref name="held"/> on equal shares, and when none has a value.
    /// </summary>
    public static int Fewest(
        IEnumerable<int> round,
        int held,
        IReadOnlyDictionary<int, StageResult> last,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(last);
        var sets = round
            .Where(i => last.TryGetValue(i, out var set) && Race.Value(set, prices) != null)
            .OrderBy(i => i == held ? 0 : 1)
            .ToList();
        return Closest(sets.Select((i, place) => (place, last[i].Result))) is { } place ? sets[place] : held;
    }
}
