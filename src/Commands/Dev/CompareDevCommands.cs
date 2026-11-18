using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

internal static class CompareDevCommands
{
    /// <summary>
    /// Developer checks: any two saved runs compared, and a saved run copied as if from another build.
    /// </summary>
    public static void Add(List<Subcommand> subs) =>
        subs.AddRange([
            new(
                "compare-runs",
                "cs2dev compare-runs <run a> <run b> [metric=<key>] [unit=…] [method=independent|paired] [summary]",
                ["metric", "unit", "method"],
                ["summary"],
                Diff,
                Purpose: "Compare two saved runs as text"
            ),
            new(
                "stub-build",
                "cs2dev stub-build <run id> <game build>",
                [],
                [],
                StubBuild,
                Purpose: "Copy a saved run as if from another game build"
            ),
        ]);

    private static void Diff(CommandCall call, CommandArgs args)
    {
        Run Read(int i) =>
            RunFinder.Saved(args.Word(i) ?? throw new CommandException("usage: cs2dev compare-runs <run a> <run b>"))
            ?? throw new CommandException($"no saved run '{args.Word(i)}'");
        var a = Read(0);
        var b = Read(1);
        var method = args.String("method")?.ToLowerInvariant() switch
        {
            null => Differences.Method,
            "independent" => DifferenceMethod.Independent,
            "paired" => DifferenceMethod.Paired,
            var other => throw new CommandException($"method={other}: use independent or paired"),
        };
        var metric = args.String("metric") ?? Metrics.Kills;
        var unit = args.Unit("unit", TimeUnit.Hour);
        var prices = LiveGame.Prices(LiveGame.Require());
        var diffs = Differences.Of(a, b, metric, unit, prices, method);
        var withDelta = diffs.Where(d => d.Delta != null && double.IsFinite(d.Delta.High - d.Delta.Low)).ToList();
        // Widths relative to A, so targets of every size weigh alike.
        var widths = withDelta
            .Where(d => d.A!.Point > 0)
            .Select(d => (d.Delta!.High - d.Delta.Low) / d.A!.Point)
            .Where(double.IsFinite)
            .Order()
            .ToList();
        var median = widths.Count == 0 ? double.NaN : widths[widths.Count / 2];
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{a.Id} → {b.Id} metric={Metrics.Resolve(a, metric)} method={ResultText.Name(method)} targets={diffs.Count} with-difference={withDelta.Count} better={diffs.Count(d => d.Verdict == Model.Verdict.Better)} worse={diffs.Count(d => d.Verdict == Model.Verdict.Worse)} median-relative-width={Fmt.N(median)}"
            )
        );
        if (!args.Flag("summary"))
            foreach (var d in diffs)
                text.Append('\n').Append(CompareRowText.Row(d));
        call.Ok(text.ToString());
    }

    private static void StubBuild(CommandCall call, CommandArgs args)
    {
        var id = args.Word(0) ?? throw new CommandException("usage: cs2dev stub-build <run id> <game build>");
        var build = args.Word(1) ?? throw new CommandException("usage: cs2dev stub-build <run id> <game build>");
        var run = RunFinder.Saved(id) ?? throw new CommandException($"no saved run '{id}'");
        run.Id = RunIds.Next(DateTime.Now, RunStore.Shared.List().Select(h => h.Id));
        run.GameBuild = build;
        run.Cs2Mvid = null;
        RunStore.Shared.Write(run);
        call.Ok($"{run.Id}: a copy of {id} as if from game build {build} and an earlier CS2");
    }
}
