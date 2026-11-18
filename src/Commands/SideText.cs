using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// How replies print a side's changes: one <c>changes A:</c> or <c>changes B:</c> block per side that has any, each
/// line the change, its state and its reason, then the side's <c>gear</c> line.
/// </summary>
internal static class SideText
{
    /// <summary>
    /// Checks each side that has changes, one after the other; <paramref name="done"/> gets the checks (null for a
    /// side without changes) or the error.
    /// </summary>
    public static void Check(Setup a, Setup? b, Action<SideCheck?, SideCheck?, Exception?> done)
    {
        One(
            a,
            (checkA, errorA) =>
            {
                if (errorA != null)
                    done(null, null, errorA);
                else
                    One(b, (checkB, errorB) => done(checkA, checkB, errorB));
            }
        );
    }

    private static void One(Setup? side, Action<SideCheck?, Exception?> done)
    {
        if (side?.Changes is not { } changes)
        {
            done(null, null);
            return;
        }
        RunService.CheckSide(
            side,
            changes,
            (check, error) =>
                done(check, check == null ? error ?? new InvalidOperationException("the check failed") : null)
        );
    }

    /// <summary>Both sides' blocks; "" when neither side has changes.</summary>
    public static string Checks(SideCheck? a, SideCheck? b) =>
        string.Join('\n', new[] { Block('A', a), Block('B', b) }.Where(t => t.Length > 0));

    private static string Block(char side, SideCheck? check)
    {
        if (check == null)
            return "";
        var game = LiveGame.Require();
        string Name(string id) => id == Change.None ? Change.None : LiveGame.NameOf(game, id);
        var text = new StringBuilder(Outcomes(side, check.Changes.Outcomes));
        var gear = check.Changes.Gear;
        text.Append(CultureInfo.InvariantCulture, $"\ngear {side}: ")
            .Append(gear.Items.Count == 0 ? "nothing worn" : string.Join(", ", gear.Items.Select(Name)))
            .Append(
                CultureInfo.InvariantCulture,
                $"  style={gear.Style ?? "?"} stance={gear.Stance ?? "?"} food={(gear.Food is { } f ? Name(f) : "none")}"
            );
        return text.ToString();
    }

    /// <summary>A side's <c>changes</c> block: the counts, then each change with its state and reason.</summary>
    public static string Outcomes(char side, IReadOnlyList<ChangeOutcome> outcomes)
    {
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"changes {side}: {outcomes.Count(o => o.Applies)} accepted, {outcomes.Count(o => o.LeftOut)} refused, {outcomes.Count(o => o.State == ChangeState.NoChange)} no change"
            )
        );
        foreach (var outcome in outcomes)
        {
            text.Append("\n  ").Append(outcome.Change.ShortForm).Append(' ').Append(ChangeStates.Name(outcome.State));
            if (outcome.From != null)
                text.Append(" was=").Append(outcome.From == Change.None ? Change.None : Ids.Brief(outcome.From));
            if (outcome.Reasons.Count > 0)
                text.Append(' ').Append(outcome.Reasons.English("; "));
        }
        return text.ToString();
    }
}
