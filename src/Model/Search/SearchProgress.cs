namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// How far a search has come, as one share from 0 to 1, from what it knows as it goes: neither how many sets a stage
/// will drop nor how many passes a climb will take is known ahead, so both are estimated, and the largest share
/// given so far is kept, so a bar of it never runs back.
/// </summary>
public sealed class SearchProgress
{
    /// <summary>
    /// The step a search shows: a search a slot at a time counts its steps, each ended by its last stage, so the one
    /// under way is one past those ended; one that fights every set counts its stages.
    /// </summary>
    public static int Step(bool climb, int ended, int stage) => climb ? ended + 1 : stage;

    /// <summary>
    /// The best set among <paramref name="results"/> (current gear's among them) with its gain over current gear,
    /// as a stop now would keep it; null while current gear leads or nothing finished.
    /// </summary>
    public static (int Index, Estimate? Percent)? BestSoFar(
        IReadOnlyList<StageResult> results,
        Goal goal,
        double share,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(goal);
        var end = Race.End(results, goal, share, prices);
        return end is { State: AnswerState.Found, Best: { } best } ? (best, end.Percent) : null;
    }

    /// <summary>
    /// The seconds a search has left, from its pace so far: <paramref name="elapsed"/> seconds for the share
    /// <paramref name="shown"/>; a climb stops at the limit, so no more than <paramref name="toLimit"/> when it
    /// climbs, and the time to that limit until its pace says less. Null while too little is done to say.
    /// </summary>
    public static double? SecondsLeft(double elapsed, double shown, double? toLimit)
    {
        if (elapsed < EstimateAfter)
            return null;
        // A climb's bar counts every pass it may make, so it starts slow: its first steps say little of its pace.
        double? left = shown < 0.02 ? null : elapsed * (1 - Math.Min(shown, 1)) / shown;
        if (toLimit is not { } most)
            return left;
        return Math.Clamp(left ?? most, 0, Math.Max(0, most));
    }

    /// <summary>
    /// The seconds the round under way still needs at its own pace: <paramref name="elapsed"/> seconds for its share
    /// <paramref name="round"/>. A climb stops at its limit only between steps, so this is the least it has left.
    /// </summary>
    public static double? RoundLeft(double elapsed, double round) =>
        round < 0.02 ? null : elapsed * (1 - Math.Min(round, 1)) / round;

    /// <summary>
    /// The bar follows the time left once there is one: its share is no less than the time spent over the time spent
    /// and left, so a search near its limit doesn't show a short bar beside a few seconds left. It never runs back.
    /// </summary>
    public double Follow(double elapsed, double? left)
    {
        if (left is { } l && elapsed + l > 0)
            Shown = Math.Max(Shown, Math.Clamp(elapsed / (elapsed + l), 0, 1));
        return Shown;
    }

    /// <summary>
    /// The seconds left, and the bar following them: at the search's pace, <paramref name="shown"/> done
    /// (<see cref="Update"/>'s share) <paramref name="elapsed"/> seconds in. A
    /// climb (<paramref name="climbSpent"/> seconds into its own, <paramref name="queued"/> more after it) is no more
    /// than its limit and each queued climb's, and no less than its step under way needs at that step's pace
    /// (<paramref name="roundElapsed"/> seconds in).
    /// </summary>
    public double? Left(double shown, double elapsed, double? climbSpent, int queued, double roundElapsed, double limit)
    {
        var toLimit = climbSpent is { } spent ? Math.Max(0, limit * (1 + queued) - spent) : (double?)null;
        var left = SecondsLeft(elapsed, shown, toLimit);
        if (toLimit != null && left != null && RoundLeft(roundElapsed, LastRound) is { } step)
            left = Math.Max(left.Value, step);
        Follow(elapsed, left);
        return left;
    }

    /// <summary>The round under way's share done, as the last update found it.</summary>
    public double LastRound { get; private set; }

    /// <summary>How long a search runs before its pace says anything.</summary>
    public const double EstimateAfter = 5;

    private double _climbFrom;
    private double _climbShare = 1;

    // The trials the round's finished stages fought, over all their sets.
    private long _roundDone;

    /// <summary>A round starts: nothing of it fought yet.</summary>
    public void RoundStarts() => _roundDone = 0;

    /// <summary>A stage of the round ends, its <paramref name="sets"/> fought <paramref name="trials"/> trials each.</summary>
    public void StageDone(int sets, int trials) => _roundDone += (long)sets * trials;

    /// <summary>The share shown: the largest given so far.</summary>
    public double Shown { get; private set; }

    /// <summary>
    /// The share now: the round under way at <paramref name="stage"/> of <paramref name="trials"/>, with
    /// <paramref name="left"/> sets of which <paramref name="fought"/> are fought, within the climb when there is one.
    /// </summary>
    public double Update(IReadOnlyList<int> trials, int stage, int left, int fought, Climb? climb)
    {
        var round = LastRound = Round(trials, stage, _roundDone, left, fought);
        var now =
            climb == null
                ? round
                : _climbFrom
                    + _climbShare * Climb(climb.Passes, climb.Step, climb.StepsInPass, round, Search.Climb.MostPasses);
        Shown = Math.Max(Shown, Math.Clamp(now, 0, 1));
        return Shown;
    }

    /// <summary>A climb starts: it takes its part of what is left of the bar, with <paramref name="queued"/> after it.</summary>
    public void ClimbStarts(int queued)
    {
        _climbFrom = Shown;
        _climbShare = Share(Shown, queued);
    }

    /// <summary>The share of a stage's sets expected to reach the next, as races of a search usually go.</summary>
    public const double Kept = 0.25;

    /// <summary>
    /// A round's share done: the trials fought over the trials expected. <paramref name="stage"/> is the stage
    /// under way, from 1; <paramref name="done"/> the trials its earlier stages fought, over all their sets;
    /// <paramref name="left"/> the sets in it and <paramref name="fought"/> those it has fought.
    /// </summary>
    public static double Round(IReadOnlyList<int> trials, int stage, long done, int left, int fought)
    {
        ArgumentNullException.ThrowIfNull(trials);
        if (stage < 1 || stage > trials.Count)
            return 0;
        var now = (double)fought * trials[stage - 1];
        var rest = (double)Math.Max(0, left - fought) * trials[stage - 1];
        var sets = (double)left;
        for (var s = stage; s < trials.Count; s++)
        {
            sets = Math.Max(1, sets * Kept);
            rest += sets * trials[s];
        }
        var total = done + now + rest;
        return total <= 0 ? 0 : (done + now) / total;
    }

    /// <summary>
    /// A climb's share done: each pass an equal share of the most it may make, and each step an equal share of its
    /// pass, the step under way counted by <paramref name="round"/>. A climb that stops early jumps to its end.
    /// </summary>
    public static double Climb(int pass, int step, int steps, double round, int mostPasses)
    {
        if (steps <= 0 || mostPasses <= 0)
            return 0;
        var inPass = (Math.Min(step, steps - 1) + Math.Clamp(round, 0, 1)) / steps;
        return Math.Clamp((Math.Max(0, pass - 1) + inPass) / mostPasses, 0, 1);
    }

    /// <summary>
    /// The share of what is left that a climb starting at <paramref name="from"/> fills: an equal part with the
    /// climbs queued after it, less a fifth kept for a climb from its runner-up, which may follow.
    /// </summary>
    public static double Share(double from, int queued) =>
        (1 - Math.Clamp(from, 0, 1)) * 0.8 / (1 + Math.Max(0, queued));
}
