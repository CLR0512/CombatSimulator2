using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// How long a search's fights took on this machine in this session, per kind of target, so the next search can
/// say how long it expects to take before it starts. Measured from fights only; without a measurement nothing is
/// expected.
/// </summary>
internal static class SearchTimes
{
    public const double LongSearchSeconds = PlanPreview.LongSearchSeconds;

    /// <summary>
    /// The long-search limit as searches under way keep to it: a search that fights every set gives up on its first
    /// stage past it, and a second climb stops at it. Lowered only by a dev command, to see either happen.
    /// </summary>
    public static double LimitSeconds { get; set; } = LongSearchSeconds;

    /// <summary>
    /// How much longer a trial of a search that fights every set takes than one of a search a slot at a time: the
    /// slot at a time fights sets near the best, which end their fights sooner than the weak ones a whole search
    /// fights too. Three times, as a whole search of a slow target measured against its slot at a time.
    /// </summary>
    public const double WholeOverSteps = 3;

    // Fights timed, by target and by kind of target, apart for searches that fight every set and those a slot at a
    // time. Targets of one kind differ widely (a weak enemy dies at once, a strong one takes minutes), so a target's
    // own timing is used once it has one, the kind's before.
    private static readonly Dictionary<(string Scope, bool Whole), (double Seconds, double Trials)> _fought = [];

    /// <summary>
    /// Notes a stage against <paramref name="target"/> (a key) of <paramref name="kind"/>: <paramref name="sets"/>
    /// sets fought <paramref name="trials"/> trials each, counting only the sets it fought, not those whose results it
    /// already had; <paramref name="whole"/> when the search fights every set.
    /// </summary>
    public static void Record(string target, TargetKind kind, int sets, int trials, double seconds, bool whole)
    {
        if (sets <= 0 || trials <= 0 || seconds <= 0)
            return;
        foreach (var scope in new[] { Of(target), Of(kind) })
        {
            var (total, fought) = _fought.GetValueOrDefault((scope, whole));
            _fought[(scope, whole)] = (total + seconds, fought + (double)sets * trials);
        }
    }

    /// <summary>
    /// Seconds a trial of a search that fights every set is expected to take on <paramref name="target"/>: over every
    /// such stage against it this session, so one small or odd stage doesn't sway it, else from its slot-at-a-time
    /// steps, <see cref="WholeOverSteps"/> times as long; before it has been fought, the same from its kind's; null
    /// before any was measured.
    /// </summary>
    public static double? PerTrial(string target, TargetKind kind) => PerTrial(Of(target)) ?? PerTrial(Of(kind));

    private static double? PerTrial(string scope) =>
        Seconds(scope, whole: true) ?? Seconds(scope, whole: false) * WholeOverSteps;

    private static double? Seconds(string scope, bool whole) =>
        _fought.TryGetValue((scope, whole), out var total) && total.Trials > 0 ? total.Seconds / total.Trials : null;

    private static string Of(string target) => "target:" + target;

    private static string Of(TargetKind kind) => "kind:" + kind;

    /// <summary>
    /// The seconds a search of <paramref name="sets"/> sets is expected to take at <paramref name="perTrial"/>
    /// seconds a trial. Every set fights the first stage; the later ones are counted as a search's usually go: a
    /// sixth of the sets reaching the second and a twentieth the third, or after a short first stage a third, a
    /// tenth and a thirtieth. <paramref name="foughtOnce"/> counts a race fought once, where a set costs the trials
    /// of the stage it leaves at, not those of every stage it reached.
    /// </summary>
    public static double Expected(double perTrial, int sets, int trials, bool foughtOnce = false)
    {
        var stages = Answers.Stages(trials, sets);
        double[] reaching = stages[0] == Answers.FirstRung ? [1, 1 / 3.0, 1 / 10.0, 1 / 30.0] : [1, 1 / 6.0, 1 / 20.0];
        var count = Math.Min(stages.Count, reaching.Length);
        double fought = 0;
        for (var i = 0; i < count; i++)
        {
            var leaving = foughtOnce && i < count - 1 ? reaching[i] - reaching[i + 1] : reaching[i];
            fought += sets * leaving * stages[i];
        }
        return fought * perTrial;
    }
}
