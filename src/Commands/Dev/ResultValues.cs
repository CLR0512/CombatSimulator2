using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// <c>cs2dev values</c>: a hash over a fixed list of each result's values, so it stays equal when results gain
/// members and shows that the ones listed here didn't change.
/// </summary>
internal static class ResultValues
{
    public static Subcommand Sub { get; } =
        new(
            "values",
            "cs2dev values [run id] [against=<run id>]",
            ["against"],
            [],
            Print,
            NeedsGame: false,
            Purpose: "One hash over a fixed list of each result's values, leaving out targets a trial ran over in; against: how many of their first batches equal the other run's"
        );

    private static void Print(CommandCall call, CommandArgs args)
    {
        var run = RunFinder.CurrentOrSaved(args.Word(0)) ?? throw new CommandException("no such run");
        var results = run.Targets.Select(k => run.Results[k]).ToList();
        var leftOut = results.Where(RanOver).ToList();
        var parts = results.Select(r => RanOver(r) ? (object)new { r.Key } : Projection(r));
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(parts, RunFile.Options))
        )[..12];
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{run.Id} values {hash} of {results.Count} targets, {leftOut.Count} left out"
            )
        );
        foreach (var r in leftOut)
            text.Append(CultureInfo.InvariantCulture, $"\nleft out {r.Key} {ResultText.Name(r.State)}");
        if (args.String("against") is { } otherId)
        {
            var other = RunFinder.CurrentOrSaved(otherId) ?? throw new CommandException($"no run '{otherId}'");
            foreach (var r in leftOut)
                text.Append('\n').Append(Against(r, other));
        }
        call.Ok(text.ToString());
    }

    // A trial over its limit ends the target or is counted in it, by rules that may change; neither is a value
    // the hash can hold steady.
    private static bool RanOver(TargetResult r) => r.State == TargetState.Unfinished || r.RanOver > 0;

    private static object Projection(TargetResult r) =>
        new
        {
            r.Key,
            r.State,
            r.TrialsDone,
            Batches = r.Batches.Select(Batch),
            Fights = r.Fights.Select(f => new
            {
                f.Trial,
                f.Stage,
                f.Enemy,
                f.GameSeconds,
                f.DamageTaken,
                f.Outcome,
                f.MaxHit,
                f.MinHp,
            }),
        };

    private static object Batch(Tally b) =>
        new
        {
            b.Trials,
            b.Kills,
            b.Deaths,
            b.GameSeconds,
            b.Xp,
            b.Gained,
            b.Currency,
            b.Used,
            b.RareEvents,
            b.MaxHit,
            b.MinHp,
            b.CloseCalls,
        };

    private static string Against(TargetResult mine, Run other)
    {
        if (!other.Results.TryGetValue(mine.Key, out var theirs))
            return $"against {mine.Key}: not in {other.Id}";
        var a = mine.Batches.Select(BatchJson).ToList();
        var b = theirs.Batches.Select(BatchJson).ToList();
        var equal = a.Zip(b).TakeWhile(pair => pair.First == pair.Second).Count();
        var firstOver = mine.Batches.FindIndex(t => t.RanOver > 0);
        var verdict =
            firstOver < 0 ? "no ran-over batch"
            : equal >= firstOver ? $"equal before the first ran-over batch ({firstOver})"
            : $"DIFFERS before the first ran-over batch ({firstOver})";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"against {mine.Key}: first {equal} batches equal, of {a.Count} here and {b.Count} in {other.Id}: {verdict}"
        );
    }

    private static string BatchJson(Tally b) => JsonSerializer.Serialize(Batch(b), RunFile.Options);
}
