using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Commands;

// A setup search's lines beyond gear: the loadout it was found for, a line per choice with what it uses, the sets
// beside the best and how ties were broken. A gear search's reply has none of them.
internal static partial class GearText
{
    private static bool IsSetup(Answer answer) => answer.Key.Kinds.Count > 0;

    // ", for Loadout 2" after the best's gain, when the search ran on a loadout rather than the character as it is.
    private static string For(Answer answer) =>
        IsSetup(answer) && answer.Loadout is { Length: > 0 } loadout && loadout != Setup.Current
            ? ", for " + loadout
            : "";

    /// <summary>A line per choice the set holds: old → new with its mark and supplies, or keep.</summary>
    public static void Choices(StringBuilder text, SetResult set)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(set);
        foreach (var choice in set.Choices)
        {
            text.Append("\n  ").Append(Label(choice)).Append(": ");
            if (choice.Keep)
            {
                text.Append("keep");
                continue;
            }
            text.Append(Names(choice.OldNames)).Append(" → ").Append(Names(choice.NewNames));
            if (MarkText(choice.Mark) is { Length: > 0 } mark)
                text.Append("  [").Append(mark).Append(']');
            if (choice.Supplies.Count > 0)
                text.Append("   ").Append(string.Join(", ", choice.Supplies.Select(Supply)));
        }
    }

    /// <summary>Cheaper, Safer and how ties were broken, after the best's lines.</summary>
    public static void Efficiency(StringBuilder text, Answer answer)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(answer);
        if (!IsSetup(answer) || answer.Best is not { } best)
            return;
        if (answer.Alternatives?.Cheaper is { } cheaper)
            text.Append("\ncheaper: ").Append(Against(cheaper, best, safer: false));
        if (answer.Alternatives?.Safer is { } safer)
            text.Append("\nsafer: ").Append(Against(safer, best, safer: true));
        // A runner-up that changes nothing in these fights (the same result to the last hit) isn't worth a line.
        foreach (
            var call in answer.CloseCalls.Where(c =>
                c.Outcome != CloseCallOutcome.Kept && c.Difference is not { Low: 0, High: 0 }
            )
        )
            text.Append('\n')
                .Append(call.Outcome == CloseCallOutcome.Swapped ? "close call won by " : "also fine: ")
                .Append(call.RunnerUp)
                .Append(" instead of ")
                .Append(call.Kept)
                .Append(
                    call.Difference is { } d
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $", {Signed(d.Point)}% [{d.Low * 100:0.0}, {d.High * 100:0.0}]"
                        )
                        : ""
                );
        if (answer.StoppedBeforeCloseCalls)
            text.Append("\nstopped before its close calls were settled");
        foreach (var gap in answer.WinsThroughGap)
            text.Append("\nthe winner relies on something not simulated: ").Append(gap.English());
    }

    // What an alternative gives up against the best, and what it saves.
    private static string Against(SetResult set, SetResult best, bool safer)
    {
        var parts = new List<string>();
        if (set.Percent is { } percent && best.Percent is { } top)
            parts.Add(Signed(percent.Point - top.Point) + "%");
        // Safer is about damage: the supplies it saves or swaps are Cheaper's to show.
        foreach (var cost in safer ? [] : best.Supplies)
        {
            var less = cost.PerHour.Point - SupplyCosts.Point(set.Supplies, cost.Key);
            if (less > 0)
                parts.Add($"{Fmt.N(less)} fewer {SupplyName(cost)} /h");
        }
        if (safer && set.DamageTaken is { } taken && best.DamageTaken is { Point: > 0 } was)
            parts.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(1 - taken.Point / was.Point) * 100:0}% less damage taken"
                )
            );
        var changes = Line(set);
        return string.Join(" · ", parts) + (changes.Length > 0 ? "   (" + changes + ")" : "");
    }

    private static string Label(ChoiceChange choice) =>
        choice.Kind switch
        {
            ChoiceKind.Prayers => "Prayers",
            ChoiceKind.Spell => "Spell",
            ChoiceKind.Imbuement => "Imbuement",
            ChoiceKind.Food => "Food",
            ChoiceKind.Potion => "Potion",
            _ when choice.Name is { Length: > 0 } slot && char.IsDigit(slot[^1]) => "Utility " + slot[^1],
            _ => choice.Name,
        };

    private static string Names(List<string> names) => names.Count == 0 ? "none" : string.Join(", ", names);

    private static string MarkText(Mark mark) =>
        mark.Kind switch
        {
            MarkKind.NotInBank => "not in your bank",
            MarkKind.Locked => "needs " + (mark.Needs?.English() ?? "something"),
            _ => "",
        };

    private static string Supply(SupplyCost cost) => $"{SupplyName(cost)} {Fmt.N(cost.PerHour.Point)} /h";

    private static string SupplyName(SupplyCost cost) =>
        (cost.Name?.English() ?? Ids.Brief(cost.Key[(cost.Key.LastIndexOf(':') + 1)..])).ToLowerInvariant();

    // A share as a signed percentage to one place; one that rounds to nothing has no sign.
    private static string Signed(double share)
    {
        var rounded = Math.Round(share * 100, 1);
        return rounded == 0 ? "0.0" : rounded.ToString("+0.0;−0.0", CultureInfo.InvariantCulture);
    }
}
