namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// One dungeon stage over a result's finished trials. <c>Enemies</c>: the enemies the game rolled for it, with how many
/// fights each, most first. <c>Reached</c>: trials that fought it (0: none did). Means are over those fights, null when
/// none. <c>MaxHit</c> and <c>MinHp</c> (survived fights only) are null for fights recorded before they were kept.
/// <c>Counts</c>: the means of what the game reported per fight; null for fights recorded before they were kept.
/// <c>RanOver</c>: fights the trial limit stopped.
/// </summary>
public sealed record StageRow(
    int Stage,
    IReadOnlyList<(string Enemy, int Count)> Enemies,
    int Reached,
    double? MeanSeconds,
    double? MeanDamage,
    int Deaths,
    long? MaxHit,
    long? MinHp,
    StageCounts? Counts = null,
    int RanOver = 0
);

/// <summary>A stage's attacks and hits per fight, both ways, and the damage the player dealt.</summary>
public sealed record StageCounts(double Attacks, double Hits, double Dealt, double EnemyAttacks, double EnemyHits);

/// <summary>
/// A dungeon fight by fight, from its fight records. A trial that died stops at the stage it died in, so each stage
/// is reached by the trials that reached the one before less its deaths, and the stages' deaths add up to the
/// dungeon's. <c>BetweenFightsSeconds</c>: game time not spent fighting (spawn gaps), per completion; null with none.
/// </summary>
public sealed record StageBreakdown(IReadOnlyList<StageRow> Rows, double? BetweenFightsSeconds)
{
    /// <summary>
    /// The stages of <paramref name="result"/>, numbered from 1; with <paramref name="stageCount"/>, stages no trial
    /// reached are listed too. Fights of trials not done (a cancelled target's last) are left out.
    /// </summary>
    public static StageBreakdown Of(TargetResult result, int? stageCount)
    {
        ArgumentNullException.ThrowIfNull(result);
        var fights = result.Fights.Where(f => f.Trial < result.TrialsDone).ToList();
        if (fights.Count == 0 && stageCount is not > 0)
            return new StageBreakdown([], null);
        var byStage = fights.GroupBy(f => f.Stage).ToDictionary(g => g.Key, g => g.ToList());
        var last = Math.Max(stageCount ?? 0, byStage.Count == 0 ? 0 : byStage.Keys.Max());
        var rows = new List<StageRow>(last);
        for (var stage = 1; stage <= last; stage++)
            rows.Add(Row(stage, byStage.GetValueOrDefault(stage) ?? []));
        var completions = result.Batches.Sum(b => b.Kills);
        double? between =
            completions > 0
                ? (result.Batches.Sum(b => b.GameSeconds) - fights.Sum(f => f.GameSeconds)) / completions
                : null;
        return new StageBreakdown(rows, between);
    }

    private static StageRow Row(int stage, List<FightRecord> fights)
    {
        if (fights.Count == 0)
            return new StageRow(stage, [], 0, null, null, 0, null, null);
        var enemies = fights
            .GroupBy(f => f.Enemy, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(e => e.Item2)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        var survived = fights.Where(f => f.Outcome == FightOutcome.Kill && f.MinHp != null).ToList();
        var counted = fights.Where(f => f.Attacks != null).ToList();
        var counts =
            counted.Count == 0
                ? null
                : new StageCounts(
                    counted.Average(f => (double)f.Attacks!),
                    counted.Average(f => (double)(f.Hits ?? 0)),
                    counted.Average(f => (double)(f.DamageDealt ?? 0)),
                    counted.Average(f => (double)(f.EnemyAttacks ?? 0)),
                    counted.Average(f => (double)(f.EnemyHits ?? 0))
                );
        return new StageRow(
            stage,
            enemies,
            fights.Count,
            fights.Average(f => f.GameSeconds),
            fights.Average(f => (double)f.DamageTaken),
            fights.Count(f => f.Outcome == FightOutcome.Death),
            fights.Max(f => f.MaxHit),
            survived.Count > 0 ? survived.Min(f => f.MinHp) : null,
            counts,
            fights.Count(f => f.Outcome == FightOutcome.RanOver)
        );
    }
}
