using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// <c>cs2 compare</c>: two setups on the same targets, each with its own changes, or the setup now against a saved
/// run; and the comparisons kept from earlier.
/// </summary>
internal static class Cs2CompareCommands
{
    public static void Add(List<Subcommand> subs) =>
        subs.Add(
            new(
                "compare",
                "cs2 compare <target…|current|selection|all> a=<setup> b=<setup> [a.<change>…] [b.<change>…] [ignore-req=a|b|both] [trials=1000] [seed=<u64>] [copies=<n>] [where=host|game] [fresh] [stop=<percent>]"
                    + "\ncs2 compare now <run id> [setup=<id>] [ignore-req] [fresh] [copies=<n>] [where=host|game]"
                    + "\ncs2 compare show [comparison id] [target] [metric=<key>] [unit=kill|second|minute|hour|day] [filter=all|better|worse] [order=diff|a|b]"
                    + "\ncs2 compare list | cs2 compare remove <comparison id>"
                    + "\ncs2 compare off",
                [
                    "a",
                    "b",
                    "ignore-req",
                    "trials",
                    "seed",
                    "copies",
                    "where",
                    "stop",
                    "setup",
                    "metric",
                    "unit",
                    "filter",
                    "order",
                    .. ChangeArgs.Options,
                ],
                ["fresh", "ignore-req"],
                Compare
            )
        );

    private static void Compare(CommandCall call, CommandArgs args)
    {
        switch (args.Word(0)?.ToLowerInvariant())
        {
            case "show":
                call.Ok(Show(args));
                return;
            case "off":
                PanelState.SetCompareWith(null);
                call.Ok("comparison left");
                return;
            case "list":
                call.Ok(List());
                return;
            case "remove":
                var id = args.Word(1) ?? throw new CommandException("usage: cs2 compare remove <comparison id>");
                if (!RunService.Instance.RemoveKept(id))
                    throw new CommandException($"no kept comparison '{id}' (see cs2 compare list)");
                call.Ok($"{id} removed");
                return;
            case "now":
                Now(call, args);
                return;
            default:
                TwoSetups(call, args);
                return;
        }
    }

    private static void TwoSetups(CommandCall call, CommandArgs args)
    {
        if (args.Words.Count == 0)
            throw new CommandException("usage: cs2 compare <target…|all> a=<setup> b=<setup> (see cs2 help)");
        if (args.String("a") is not { } a || args.String("b") is not { } b)
            throw new CommandException("name both sides: a=<setup> b=<setup> (see cs2 setups)");
        (bool A, bool B) ignore = args.String("ignore-req")?.ToLowerInvariant() switch
        {
            null => args.Flag("ignore-req") ? (true, true) : (false, false),
            "a" => (true, false),
            "b" => (false, true),
            "both" => (true, true),
            var other => throw new CommandException($"ignore-req={other}: use a, b or both"),
        };
        // Neither side is the default: a change says which side it is on.
        var (changesA, changesB) = ChangeArgs.From(args, null);
        var setupA = RunArgs.SetupOf(a, ignore.A);
        setupA.Changes = changesA;
        var setupB = RunArgs.SetupOf(b, ignore.B);
        setupB.Changes = changesB;
        RunService.Instance.StartSides(
            RunArgs.RequestFrom(args, setupA, args.Words),
            setupB,
            start =>
            {
                var checks = SideText.Checks(start.A, start.B);
                var text = checks.Length > 0 ? checks + "\n" : "";
                if (start.Comparison is { } comparison)
                    call.Ok(text + Started(comparison));
                else
                    call.Error($"{text}the sides are the same: {start.Run!.Id} started as a single run");
            },
            error => call.Error(error.Message)
        );
    }

    private static void Now(CommandCall call, CommandArgs args)
    {
        var id = args.Word(1) ?? throw new CommandException("usage: cs2 compare now <run id> (see cs2 runs)");
        var baseline = RunFinder.Saved(id) ?? throw new CommandException($"no saved run '{id}'");
        var setup = RunArgs.SetupFrom(args);
        var request = new RunRequest(
            setup,
            [],
            baseline.Trials,
            baseline.Seed,
            args.Int("copies", RunArgs.DefaultCopies(), 1, Math.Max(1, Environment.ProcessorCount)),
            RunArgs.Where(args),
            Fresh: args.Flag("fresh")
        );
        call.Ok(Started(RunService.Instance.StartAgainst(baseline, request)));
    }

    private static string Started(ComparedRuns comparison)
    {
        var text = new StringBuilder();
        foreach (var warning in comparison.Warnings)
            text.Append("warning: ").Append(warning.English()).Append('\n');
        text.Append(
            CultureInfo.InvariantCulture,
            $"comparison started: A {Label(comparison.A)}, B {Label(comparison.B)}, {comparison.Record.Targets.Count} targets, seed {comparison.Record.Seed}"
        );
        if (comparison.IsFinal)
            text.Append(CultureInfo.InvariantCulture, $"\n{comparison.Record.Id} final at once");
        return text.ToString();
    }

    private static string Label(Run run)
    {
        var label = run.Setup.Changes is { } changes
            ? ChangeNotes.Label(run.Setup.Label, changes.Count).English()
            : run.Setup.Label;
        return run.Id.Length > 0 ? $"{label} ({run.Id})" : $"{label} (waiting)";
    }

    /// <summary>Prints the comparison table for <c>cs2 compare show</c>.</summary>
    public static string Show(CommandArgs args)
    {
        string? id = null;
        string? target = null;
        foreach (var word in args.Words.Skip(1))
        {
            if (word.StartsWith(Model.Comparison.IdPrefix, StringComparison.Ordinal))
                id = word;
            else
                target = Ids.FullKey(word);
        }
        var comparison = Find(id);
        var game = LiveGame.Require();
        var prices = LiveGame.Prices(game);
        var metric = args.String("metric") ?? PanelState.Prefs.Metric;
        var unit = args.Unit("unit", PanelState.Prefs.Unit);
        var filter = args.String("filter")?.ToLowerInvariant() switch
        {
            null or "all" => CompareFilter.All,
            "better" => CompareFilter.Better,
            "worse" => CompareFilter.Worse,
            var other => throw new CommandException($"filter={other}: use all, better or worse"),
        };
        var order = args.String("order")?.ToLowerInvariant() switch
        {
            null or "diff" => CompareOrder.Difference,
            "a" => CompareOrder.A,
            "b" => CompareOrder.B,
            var other => throw new CommandException($"order={other}: use diff, a or b"),
        };
        var table = CompareTables.Of(comparison.A, comparison.B, metric, unit, prices, order, filter);
        var record = comparison.Record;
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{(record.Id.Length > 0 ? record.Id : "comparison")}  {Kind(record.Kind)}  A={Label(comparison.A)}  B={Label(comparison.B)}  seed={record.Seed}  state={ResultText.Name(comparison.State)}"
            )
        );
        foreach (var (side, run) in new[] { ('A', comparison.A), ('B', comparison.B) })
        {
            if (run.ChangeOutcomes is { Count: > 0 } outcomes)
                text.Append('\n').Append(SideText.Outcomes(side, outcomes));
        }
        text.Append("\nwarnings: ")
            .Append(comparison.Warnings.Count == 0 ? "none" : comparison.Warnings.English(" | "));
        // What each side's setup has that isn't simulated, each part once over its targets.
        foreach (var (side, run) in new[] { ('A', comparison.A), ('B', comparison.B) })
        foreach (var gap in GapNote.Merged(run.Results.Values) ?? [])
            text.Append(CultureInfo.InvariantCulture, $"\nnot simulated {side}: {gap.Name}: {gap.What.English()}");
        text.Append(
            CultureInfo.InvariantCulture,
            $"\nmetric={table.Metric} unit={TimeUnits.Name(unit)}  better={table.Better} worse={table.Worse} no-clear={table.NoClearDifference} none={table.NoDifference}"
        );
        if (target != null && !comparison.A.Targets.Contains(target, StringComparer.Ordinal))
            throw new CommandException($"the comparison has no target '{Ids.BriefKey(target)}'");
        foreach (var row in table.Rows.Where(r => target == null || r.Difference.Key == target))
            text.Append('\n').Append(CompareRowText.Row(row.Difference));
        text.Append("\nhash=").Append(Hash(comparison));
        if (target != null)
            AppendTarget(text, comparison, target, i => LiveGame.NameOf(game, i));
        return text.ToString();
    }

    // One target's risk, its fight numbers and, for a dungeon, its fights, side by side.
    private static void AppendTarget(
        StringBuilder text,
        ComparedRuns comparison,
        string target,
        Func<string, string> names
    )
    {
        foreach (var (side, run) in new[] { ("A", comparison.A), ("B", comparison.B) })
        {
            if (run.Results.GetValueOrDefault(target) is not { } result)
                continue;
            if (ResultText.RiskLine(result, "risk " + side) is { } risk)
                text.Append('\n').Append(risk);
        }
        text.Append('\n').Append(CompareRowText.FightRows(comparison.A, comparison.B, target));
        foreach (var (side, run) in new[] { ("A", comparison.A), ("B", comparison.B) })
        {
            if (run.Results.GetValueOrDefault(target) is { Kind: TargetKind.Dungeon, HasEstimates: true } result)
                text.Append('\n').Append(ResultText.FightsTable(result, names, "fights " + side));
        }
    }

    private static string Kind(ComparisonKind kind) =>
        kind switch
        {
            ComparisonKind.TwoSetups => "two-setups",
            ComparisonKind.WhatIf => "what-if",
            _ => "now-against-saved",
        };

    /// <summary>One hash over both sides: SHA-256 of A's results hash input followed by B's, 12 hex digits.</summary>
    public static string Hash(ComparedRuns comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var bytes = ResultsHashInput.Of(comparison.A).Concat(ResultsHashInput.Of(comparison.B)).ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(bytes))[..12];
    }

    // The comparison on screen, or a kept one, which is then shown as it finished.
    private static ComparedRuns Find(string? id)
    {
        var shown = RunService.Instance.Comparison;
        if (id == null)
            return shown ?? throw new CommandException("no comparison yet");
        return id == shown?.Record.Id ? shown : RunService.Instance.ShowKept(id);
    }

    // The kept comparisons, newest first, one per line.
    private static string List()
    {
        var kept = RunService.Instance.KeptComparisons;
        if (kept.Count == 0)
            return "none";
        var text = new StringBuilder();
        foreach (var entry in kept)
        {
            var record = entry.Record;
            if (text.Length > 0)
                text.Append('\n');
            text.Append(
                CultureInfo.InvariantCulture,
                $"{record.Id} {Kind(record.Kind)} A {record.A} B {record.B} targets={record.Targets.Count} {record.CreatedAt:yyyy-MM-dd HH:mm} {entry.Character ?? "?"}"
            );
            if (entry.AnotherBuild(LiveGame.GameBuild))
                text.Append(" another-build");
            if (entry.GoneRun != null)
                text.Append(" run-gone");
        }
        return text.ToString();
    }
}
