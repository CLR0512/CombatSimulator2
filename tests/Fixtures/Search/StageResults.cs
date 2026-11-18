using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>
/// Stage results for the race's tests: thirty batches of kills at a chosen pace. Two sets given the same wobble
/// move together batch by batch, as sets fought from one seed do, so their difference has a narrow range.
/// </summary>
internal static class StageResults
{
    public const string Target = "MelvorBase:Farm/MelvorBase:Cow";

    /// <summary>The wobble sets fought from the same seed share.</summary>
    public static double Shared(int batch) => Math.Sin(batch * 1.7) * 0.5;

    /// <summary>A wobble of its own, unrelated to <see cref="Shared"/>.</summary>
    public static double Own(int batch) => Math.Cos(batch * 2.9 + 1) * 0.5;

    /// <summary>
    /// A finished result: <paramref name="trials"/> trials in thirty batches, each trial a kill taking
    /// <paramref name="secondsPerKill"/> plus the batch's wobble, with <paramref name="deaths"/> deaths spread from
    /// the first batch on (a death is a trial without a kill).
    /// </summary>
    public static StageResult Set(
        int index,
        double secondsPerKill,
        Func<int, double>? wobble = null,
        int deaths = 0,
        int trials = 300,
        string metric = Metrics.Kills
    )
    {
        wobble ??= Shared;
        var per = trials / Batches.Default;
        var batches = new List<Tally>();
        for (var i = 0; i < Batches.Default; i++)
        {
            var died = Math.Min(per, Math.Max(0, deaths - i * per));
            batches.Add(
                new Tally
                {
                    Trials = per,
                    Kills = per - died,
                    Deaths = died,
                    GameSeconds = per * (secondsPerKill + wobble(i)),
                    Xp = { ["MelvorBase:Attack"] = (per - died) * 40.0 },
                }
            );
        }
        return new StageResult(
            index,
            new TargetResult
            {
                Key = Target,
                Kind = TargetKind.Enemy,
                State = TargetState.Finished,
                TrialsDone = trials,
                Batches = batches,
            },
            metric
        );
    }

    /// <summary>A set whose fights didn't end in a result: refused, or run over its limit.</summary>
    public static StageResult Without(int index, TargetState state) =>
        new(
            index,
            new TargetResult
            {
                Key = Target,
                State = state,
                Reasons = [Note.Text("no result")],
            },
            Metrics.Kills
        );

    /// <summary>
    /// <paramref name="set"/> with its fights taking <paramref name="damagePerSecond"/> a second, each batch off by up
    /// to <paramref name="noise"/> of it either way.
    /// </summary>
    public static StageResult Taking(StageResult set, double damagePerSecond, double noise = 0)
    {
        for (var i = 0; i < set.Result.Batches.Count; i++)
            set.Result.Batches[i].Fight = new FightCounts
            {
                Taken = new SideCounts
                {
                    Attack = (long)Math.Round(damagePerSecond * 10_000 * (1 + noise * Own(i) * 2)),
                },
                Seconds = 10_000,
            };
        return set;
    }
}
