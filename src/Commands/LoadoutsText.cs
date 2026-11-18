using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>Best loadouts' answer as <c>cs2 loadouts best</c> replies it, names through <c>names</c>.</summary>
internal static class LoadoutsText
{
    public static string Of(LoadoutPicksAnswer answer, ulong seed, Func<string, string> names, string? onSide)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(names);
        string Choice(string? slot) => slot == null ? "setup" : names(slot);
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Ids.BriefKey(answer.Master)} · best loadouts · {answer.Choices} choices · metric {answer.Goal.Metric} · seed {seed}"
            )
        );
        var width = answer.Picks.Count == 0 ? 0 : answer.Picks.Max(p => Ids.BriefKey(Ids.PartTarget(p.Monster)).Length);
        foreach (var pick in answer.Picks)
        {
            text.Append("\n  ").Append(Ids.BriefKey(Ids.PartTarget(pick.Monster)).PadRight(width));
            if (pick.Stuck)
            {
                text.Append("   stuck with every choice");
                continue;
            }
            text.Append("   ").Append(Choice(pick.Choice).PadRight(10));
            if (pick.Gain is { } gain)
                text.Append("   ").Append(Percent(gain));
            if (pick.Ties.Count > 0)
                text.Append("   tie with ").AppendJoin(", ", pick.Ties.Select(Choice));
            if (pick.OverLimit.Count > 0)
                text.Append("   over the death limit: ").AppendJoin(", ", pick.OverLimit.Select(Choice));
        }
        text.Append("\n  master: ")
            .Append(
                answer.Gain is { } g ? Percent(g)
                : answer.Figure == null ? "stuck"
                : "-"
            )
            .Append(' ')
            .Append(answer.Goal.Metric)
            .Append(" against the setup");
        text.Append("\n  ")
            .Append(onSide == null ? "for side B" : "on side " + onSide)
            .Append(": ")
            .Append(answer.Change?.ShortForm ?? "the setup for every monster");
        return text.ToString();
    }

    /// <summary>Every master on its best setup, best first, as <c>cs2 loadouts best status all</c> replies it.</summary>
    public static string Masters(IReadOnlyList<BestMaster> ranked, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        ArgumentNullException.ThrowIfNull(goal);
        var text = new StringBuilder(
            string.Create(CultureInfo.InvariantCulture, $"every master on its best setup · metric {goal.Metric}")
        );
        var place = 0;
        foreach (var m in ranked)
        {
            text.Append("\n  ").Append(m.Figure != null ? (++place).ToString(CultureInfo.InvariantCulture) : "-");
            text.Append(' ').Append(Ids.BriefKey(m.Master)).Append("  ");
            if (m.Figure is not { } figure)
            {
                text.Append(m.Stuck is { } stuck ? "stuck on " + Ids.BriefKey(Ids.PartTarget(stuck)) : "no figures");
                continue;
            }
            text.Append(Fmt.Range(figure));
            if (m.Gain is { } gain)
                text.Append("  ").Append(Percent(gain)).Append(" vs one setup");
            if (m.Deaths is { } deaths)
                text.Append("  deaths ").Append(Fmt.N(deaths.Point));
            text.Append("  blocks ")
                .Append(m.Blocked.Count == 0 ? "none" : string.Join(", ", m.Blocked.Select(Ids.BriefKey)));
            if (m.Block is { Cost: > 0 } block)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $" ({block.Cost:#,0} coins{(block.SlotsNeeded > 0 ? $", needs slot {block.Blocks.Count}" : "")})"
                );
            text.Append(CultureInfo.InvariantCulture, $"  {m.Swaps} on own loadouts");
        }
        return text.ToString();
    }

    private static string Percent(Estimate e) =>
        $"{(e.Point >= 0 ? "+" : "")}{Fmt.N(e.Point * 100)}% [{Fmt.N(e.Low * 100)}, {Fmt.N(e.High * 100)}]";
}
