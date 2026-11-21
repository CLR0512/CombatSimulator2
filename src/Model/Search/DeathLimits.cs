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
    /// The set that came closest when none is within the limit. Of the sets whose share of deaths isn't clearly above
    /// the smallest (their 95% ranges meet), the one best for the goal by its value, as
    /// <paramref name="lowerIsBetter"/> says; on equal values the smaller share, then the earlier set. A set without a
    /// value goes after those with one. Null without any trials.
    /// </summary>
    public static int? Closest(
        IEnumerable<(int Index, TargetResult Result, Estimate? Value)> sets,
        bool lowerIsBetter = false
    )
    {
        ArgumentNullException.ThrowIfNull(sets);
        var counted = sets.Select(s => (s.Index, s.Value, Counts: Counts(s.Result)))
            .Where(s => s.Counts.Trials > 0)
            .Select(s =>
                (
                    s.Index,
                    s.Value,
                    Share: s.Counts.Deaths / (double)s.Counts.Trials,
                    Range: Estimators.Wilson(s.Counts.Deaths, s.Counts.Trials)
                )
            )
            .ToList();
        if (counted.Count == 0)
            return null;
        var fewest = counted.OrderBy(s => s.Share).ThenBy(s => s.Index).First();
        // A share of deaths only a little above the smallest may be luck: the goal decides among those.
        return counted
            .Where(s => s.Range.Low <= fewest.Range.High)
            .OrderBy(s => s.Value is { } value ? (lowerIsBetter ? value.Point : -value.Point) : double.PositiveInfinity)
            .ThenBy(s => s.Share)
            .ThenBy(s => s.Index)
            .First()
            .Index;
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
    /// The set of <paramref name="round"/> that came closest to the limit by its last result in
    /// <paramref name="last"/> (<see cref="Closest"/>), among those with a value; <paramref name="held"/> on equal
    /// values and shares, and when none has a value.
    /// </summary>
    public static int Fewest(
        IEnumerable<int> round,
        int held,
        IReadOnlyDictionary<int, StageResult> last,
        bool lowerIsBetter,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(last);
        var sets = round
            .Where(i => last.TryGetValue(i, out var set) && Race.Value(set, prices) != null)
            .OrderBy(i => i == held ? 0 : 1)
            .ToList();
        return
            Closest(sets.Select((i, place) => (place, last[i].Result, Race.Value(last[i], prices))), lowerIsBetter)
                is { } place
            ? sets[place]
            : held;
    }
}
