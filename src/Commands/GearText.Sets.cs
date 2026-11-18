using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Commands;

internal static partial class GearText
{
    // For a search over a master: the monsters that kept a loadout of their own, on how many monsters the set helps,
    // where it is worse, and each monster's gain.
    private static void Members(StringBuilder text, Answer answer, SetResult best)
    {
        if (best.Result.Loadouts is { Count: > 0 } kept)
            text.Append("\n  kept their own loadout: ")
                .AppendJoin(", ", kept.Select(k => $"{Ids.BriefKey(k.Key)} ({Ids.Brief(k.Value)})"));
        if (answer.Current is not { } current)
            return;
        var gains = SetScore.PerMember(current.Result, best.Result, best.Metric);
        if (gains.Count == 0)
            return;
        var lower = Ranking.LowerIsBetter(best.Metric);
        text.Append("\n  helps ").Append(SetScore.Helps(gains, lower)).Append(" of ").Append(gains.Count);
        var worse = SetScore.Worse(gains, lower);
        if (worse.Count > 0)
            text.Append(" · worse on ")
                .Append(string.Join(", ", worse.Select(g => $"{Ids.BriefKey(g.Key)} ({Percent(g.Gain!)})")));
        if (answer.WantsOwnSet is { Count: > 0 } wants)
            text.Append("\n  wants its own set: ").AppendJoin(", ", wants.Select(Ids.BriefKey));
        text.Append("\n  per target:");
        foreach (var gain in gains)
            text.Append(' ').Append(Ids.BriefKey(gain.Key)).Append(' ').Append(gain.Gain is { } e ? Percent(e) : "-");
    }

    // For a search over several targets, a Droptimizer item's " · helps n of m"; "" otherwise.
    private static string HelpsNote(Answer answer, TargetResult result, string metric)
    {
        if (answer.Current is not { } current)
            return "";
        var gains = SetScore.PerMember(current.Result, result, metric);
        return gains.Count == 0
            ? ""
            : $" · helps {SetScore.Helps(gains, Ranking.LowerIsBetter(metric))} of {gains.Count}";
    }

    private static string Percent(Estimate e) =>
        $"{(e.Point >= 0 ? "+" : "")}{Fmt.N(e.Point * 100)}% [{Fmt.N(e.Low * 100)}, {Fmt.N(e.High * 100)}]";
}
