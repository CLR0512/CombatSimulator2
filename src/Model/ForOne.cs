namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// What the first drop of a rare item takes, from the game's chance alone: a doubled drop is still one drop, and a
/// player waiting on a rare item wants the first. <see cref="Each"/>: the kills (or completions) it takes on average;
/// null for a rare event. <see cref="Half"/> and <see cref="NineInTen"/>: the kills (completions, or minutes in
/// combat for a rare event) by which half, and nine in ten, of players have it. <see cref="Time"/>: the game seconds
/// it takes on average at the setup's kill rate, with its 95% range; <see cref="HalfTime"/> and
/// <see cref="NineInTenTime"/> the same for half and nine in ten.
/// </summary>
public sealed record ForOne(
    ChancePer Per,
    double? Each,
    double Half,
    double NineInTen,
    Estimate? Time,
    double? HalfTime,
    double? NineInTenTime
);

public static class ForOnes
{
    /// <summary>
    /// What one of <paramref name="drop"/> takes in <paramref name="result"/>; null without a chance above 0 or
    /// without estimates.
    /// </summary>
    public static ForOne? Of(TargetResult result, DropChance drop)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(drop);
        if (!result.HasEstimates || drop.Chance is not ({ } chance and > 0 and <= 1))
            return null;
        var half = Within(chance, 0.5);
        var nine = Within(chance, 0.9);
        if (drop.Per == ChancePer.MinuteInCombat)
        {
            var minutes = 1 / chance;
            return new ForOne(
                drop.Per,
                null,
                half,
                nine,
                new Estimate(minutes * 60, minutes * 60, minutes * 60, 1, "chance"),
                half * 60,
                nine * 60
            );
        }
        var each = 1 / chance;
        var killTime = KillTime(result);
        return new ForOne(
            drop.Per,
            each,
            half,
            nine,
            killTime?.Scale(each),
            killTime?.Point * half,
            killTime?.Point * nine
        );
    }

    /// <summary>
    /// The game seconds the first <paramref name="item"/> takes on average at the setup's kill rate, from the
    /// chances of its entries; null when they don't cover every way it drops (see
    /// <see cref="RareDrops.ExpectedPerKill"/>), as a drop the chances miss would come sooner.
    /// </summary>
    public static Estimate? Time(TargetResult result, string item)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (RareDrops.ExpectedPerKill(result, item) == null || result.Drops is not { } drops)
            return null;
        var chance = drops.Where(d => d.Source != DropSource.RareEvent && d.Id == item).Sum(d => d.Chance ?? 0);
        return chance > 0 && KillTime(result) is { } killTime ? killTime.Scale(1 / chance) : null;
    }

    // The tries by which a share of players have seen a chance come up: the geometric distribution's quantile.
    private static double Within(double chance, double share) =>
        chance >= 1 ? 1 : Math.Log(1 - share) / Math.Log(1 - chance);

    private static Estimate? KillTime(TargetResult result) =>
        Metrics.Estimate(result, Metrics.KillTime, TimeUnit.Second) is { } e
        && double.IsFinite(e.Point)
        && double.IsFinite(e.High)
            ? e
            : null;
}
