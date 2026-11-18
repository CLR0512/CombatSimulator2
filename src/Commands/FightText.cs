using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>What the game reported in a target's fights, as the lines <c>cs2 result … full</c> prints.</summary>
internal static class FightText
{
    /// <summary>
    /// The fights block: its header, the count lines and the numbers worked out from them; for an unfinished target
    /// the trials that ran over as plain values; or that the result has none recorded.
    /// </summary>
    public static string Block(TargetResult r, Func<string, string> names)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (!r.HasEstimates && r.Stall is { } stall)
            return Unfinished(stall, names);
        if (!r.HasFightCounts)
            return "fights: not recorded";
        var counts = new FightCounts();
        foreach (var b in r.Batches)
            if (b.Fight is { } fight)
                counts.Add(fight);
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"fights: {r.Batches.Sum(b => b.Trials)} trials, {r.RanOver} ran over, {counts.Seconds:0.0} s in fights"
            )
        );
        AppendCounts(text, counts);
        string N(string key, bool percent = false, string? none = null) =>
            FightNumbers.Estimate(r, key) is { } e ? Range(e, percent) : "none" + (none == null ? "" : ": " + none);
        text.Append("\n  chance to hit ")
            .Append(N(FightNumbers.Hit, true, "no attack made"))
            .Append("   damage per hit ")
            .Append(N(FightNumbers.PerHit, none: "no hit landed"))
            .Append("   damage per second ")
            .Append(N(FightNumbers.Dps));
        AppendCauses(text, r, taken: false);
        text.Append("\n  enemy chance to hit ")
            .Append(N(FightNumbers.EnemyHit, true, "it made no attack"))
            .Append("   damage per hit ")
            .Append(N(FightNumbers.EnemyPerHit, none: "no hit landed"))
            .Append("   damage taken per second ")
            .Append(N(FightNumbers.Taken));
        AppendCauses(text, r, taken: true);
        text.Append("\n  overkill ")
            .Append(N(FightNumbers.Overkill, true))
            .Append("   attacks per kill ")
            .Append(N(FightNumbers.AttacksPerKill, none: "no kill"));
        if (FightNumbers.Estimate(r, FightNumbers.MissedPerKill) is { } missed)
            text.Append(", of which ").Append(Range(missed, false)).Append(" missed");
        if (r.RanOver > 0 && FightNumbers.Estimate(r, FightNumbers.RanOver) is { } share)
        {
            text.Append("\n  ran over ").Append(Range(share, true));
            if (r.Stall is { } ranOver)
                text.Append(": ").Append(Reason(ranOver, names));
        }
        return text.ToString();
    }

    private static string Reason(Stall stall, Func<string, string> names) =>
        StallReasons.Note(stall, stall.Enemy is { } enemy ? names(enemy) : null).English();

    private static string Unfinished(Stall stall, Func<string, string> names)
    {
        var text = new StringBuilder(
            stall.Trials == 1
                ? "fights: 1 unfinished trial"
                : string.Create(CultureInfo.InvariantCulture, $"fights: {stall.Trials} unfinished trials")
        );
        AppendCounts(text, stall.Counts);
        var numbers = FightNumbers.OfStall(stall);
        string P(string key, bool percent = false) => numbers.TryGetValue(key, out var v) ? Plain(v, percent) : "none";
        text.Append("\n  chance to hit ")
            .Append(P(FightNumbers.Hit, true))
            .Append("   damage per hit ")
            .Append(P(FightNumbers.PerHit))
            .Append("   damage per second ")
            .Append(P(FightNumbers.Dps))
            .Append("\n  enemy chance to hit ")
            .Append(P(FightNumbers.EnemyHit, true))
            .Append("   damage per hit ")
            .Append(P(FightNumbers.EnemyPerHit))
            .Append("   damage taken per second ")
            .Append(P(FightNumbers.Taken))
            .Append("\n  ")
            .Append(Reason(stall, names));
        return text.ToString();
    }

    // The line under a damage per second that has effects or reflect in it.
    private static void AppendCauses(StringBuilder text, TargetResult r, bool taken)
    {
        var effects = FightNumbers.PerSecond(r, s => s.Effect, taken);
        var reflect = FightNumbers.PerSecond(r, s => s.Reflect, taken);
        if (effects == null && reflect == null)
            return;
        text.Append("\n    of which");
        if (effects != null)
            text.Append(" effects ").Append(Range(effects, false)).Append("/s");
        if (reflect != null)
            text.Append(" reflect ").Append(Range(reflect, false)).Append("/s");
    }

    private static string Range(Estimate e, bool percent) =>
        double.IsFinite(e.High)
            ? $"{Plain(e.Point, percent)} [{Plain(e.Low, false, percent)}, {Plain(e.High, false, percent)}]"
            : $"{Plain(e.Point, percent)} [{Plain(e.Low, false, percent)}, ∞]";

    private static string Plain(double value, bool percent, bool scaled = false) =>
        (percent || scaled ? value * 100 : value).ToString("0.0", CultureInfo.InvariantCulture) + (percent ? "%" : "");

    /// <summary>The two count lines, the player's and the enemy's.</summary>
    public static void AppendCounts(StringBuilder text, FightCounts counts)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(counts);
        var you = counts.Dealt;
        text.Append("\n  you:   ")
            .Append(Damage(you))
            .Append(CultureInfo.InvariantCulture, $" overkill {you.Overkill}")
            .Append(
                CultureInfo.InvariantCulture,
                $"\n         resisted {you.Resisted} ward {you.WardCut} armour {you.ArmourCut} ward-damage {you.WardDamage} enemy-healed {you.Healed}"
            );
        text.Append("\n  enemy: ").Append(Damage(counts.Taken));
    }

    /// <summary>A batch's ran-over trials, for the end of its line; empty when not recorded.</summary>
    public static string BatchSuffix(Tally b) =>
        b?.RanOver is { } n ? string.Create(CultureInfo.InvariantCulture, $" ran-over {n}") : "";

    private static string Damage(SideCounts side) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"attacks {side.Attacks} hits {side.Hits} damage {side.Damage} (attack {side.Attack} effect {side.Effect} reflect {side.Reflect})"
        );
}
