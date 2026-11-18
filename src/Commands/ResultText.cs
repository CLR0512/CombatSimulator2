using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>Results as command text: the summary, the details, the results hash, risk, fights and rare drops.</summary>
internal static partial class ResultText
{
    /// <summary>
    /// The run and one line per target in the run's order, the best by <paramref name="metric"/> marked
    /// <c>best</c>. Targets where every fight died come last under their own line, marked <c>died</c>: their
    /// values were earned while dying, so none of them is best. Targets whose trial ran over follow, under one line
    /// per reason, least enemy health left first.
    /// </summary>
    public static string Summary(Run run, TimeUnit unit, PriceTable prices, string metric = Metrics.MainXp)
    {
        var text = new StringBuilder(
            $"{run.Id} {Name(run.State)} {run.Done}/{run.Targets.Count} targets setup {run.Setup.Label} build {run.GameBuild} seed {run.Seed}"
        );
        text.Append("\nresults ").Append(ResultsHash(run));
        var best = Ranking.Rank(run, metric, unit, prices).BestKey;
        var died = run.Targets.Where(key => run.Results[key].NoneSurvived).ToList();
        var stalled = StallGroups
            .Order(
                run.Targets.Select(key => (key, mark: StallGroups.MarkOf(run.Results[key])))
                    .Where(p => p.mark != null)
                    .Select(p => new RankRow(p.key, p.key, null, null, 0, TargetState.Unfinished, null, Stall: p.mark)),
                key => run.Targets.IndexOf(key)
            )
            .ToList();
        foreach (var key in run.Targets.Except(died).Except(stalled.Select(r => r.Key), StringComparer.Ordinal))
            AppendTarget(text, run, key, unit, prices, key == best ? " best" : "");
        if (best == null && died.Count > 0)
            text.Append("\nno target survived");
        if (died.Count > 0)
            text.Append("\nevery fight died:");
        foreach (var key in died)
            AppendTarget(text, run, key, unit, prices, " died");
        foreach (var group in stalled.GroupBy(r => r.Stall!.Reason))
        {
            text.Append('\n').Append(StallReasons.Word(group.Key)).Append(':');
            foreach (var row in group)
                AppendTarget(text, run, row.Key, unit, prices, "");
        }
        return text.ToString();
    }

    private static void AppendTarget(
        StringBuilder text,
        Run run,
        string key,
        TimeUnit unit,
        PriceTable prices,
        string mark
    )
    {
        var per = TimeUnits.Name(unit);
        var r = run.Results[key];
        text.Append('\n').Append(Ids.BriefKey(key)).Append(' ').Append(Name(r.State));
        if (r.Kind == TargetKind.Master)
            AppendMaster(text, r, unit, prices);
        else if (r.HasEstimates)
        {
            var kills = Metrics.Estimate(r, Metrics.Kills, unit, prices)!;
            var deaths = Metrics.Estimate(r, Metrics.DeathRate, unit, prices)!;
            text.Append(
                CultureInfo.InvariantCulture,
                $" {(r.Kind == TargetKind.Dungeon ? "completions" : "kills")}/{per} {Fmt.Range(kills)} deaths {Fmt.Range(deaths)}"
            );
            if (Risk.Of(r) is { Warning: true })
                text.Append(" !risk");
            if (r.RanOver > 0)
                text.Append(CultureInfo.InvariantCulture, $" ran-over {r.RanOver}/{r.Batches.Sum(b => b.Trials)}");
        }
        else if (r.Reasons.Count > 0)
            text.Append(' ').Append(r.Reasons.English(" | "));
        if (r.StoppedEarly)
            text.Append(
                CultureInfo.InvariantCulture,
                $" stopped {r.TrialsDone}/{run.Trials} at ±{run.StopPrecision * 100:0.##}%"
            );
        if (r.ReusedFrom is { } origin)
            text.Append(CultureInfo.InvariantCulture, $" reused {origin.RunId} seed {origin.Seed}");
        text.Append(mark);
    }

    /// <summary>
    /// A short hash of every target's state, reasons (in English, whatever the language), batches and fights: equal
    /// hashes, equal results.
    /// </summary>
    private static string ResultsHash(Run run) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(ResultsHashInput.Of(run)))[..12];

    public static string Details(
        Run run,
        TargetResult r,
        TimeUnit unit,
        PriceTable prices,
        Func<string, string> names,
        bool full,
        string metric = Metrics.MainXp
    )
    {
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Ids.BriefKey(r.Key)} {Name(r.State)} {r.TrialsDone} trials {r.GameSeconds:0} game s {r.RealSeconds:0.00} real s"
            )
        );
        if (r.Task is { } taskMaster)
            text.Append(" on ").Append(names(taskMaster)).Append("'s task");
        if (r.TaskNote is { } note)
            text.Append(' ').Append(note.English());
        text.Append(
            CultureInfo.InvariantCulture,
            $"\nrun {run.Id} setup {run.Setup.Label} build {run.GameBuild} seed {run.Seed}"
        );
        if (r.ReusedFrom is { } origin)
            text.Append(
                CultureInfo.InvariantCulture,
                $"\nreused {origin.RunId} seed {origin.Seed} from {origin.Date:yyyy-MM-dd HH:mm} UTC"
            );
        foreach (var reason in r.Reasons)
            text.Append("\nreason: ").Append(reason.English());
        if (r.Kind == TargetKind.Master)
            AppendMasterDetails(text, run, r, unit, prices, metric);
        var master = r is { Kind: TargetKind.Master, State: TargetState.Finished };
        if (r.HasEstimates || master)
        {
            text.Append("\nper ").Append(TimeUnits.Name(unit)).Append(" (untimed metrics as they are)");
            foreach (var v in Metrics.All(r, unit, prices, names))
                text.Append('\n')
                    .Append(v.Metric.Key)
                    .Append(' ')
                    .Append(Fmt.Range(v.Estimate))
                    .Append("  ")
                    .Append(v.Metric.Label);
            if (!master)
            {
                var needed = Metrics.TrialsFor5Percent(r);
                text.Append("\ntrials for ±5%: ").Append(needed == int.MaxValue ? "∞" : Fmt.I(needed));
            }
        }
        text.Append("\nassumptions:");
        foreach (var a in ChangeNotes.Assumptions(run, r, names))
            text.Append("\n  ").Append(a.English());
        foreach (var gap in r.NotSimulated ?? [])
            text.Append("\nnot simulated: ").Append(gap.Name).Append(": ").Append(gap.What.English());
        text.Append("\nunsupported:");
        foreach (var u in r.Unsupported)
            text.Append("\n  ").Append(u.English());
        if (full)
        {
            text.Append("\nbatches:");
            foreach (var b in r.Batches)
            {
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"\n  trials {b.Trials} kills {b.Kills} deaths {b.Deaths} game s {b.GameSeconds:0.0}"
                );
                foreach (var (k, v) in b.Xp)
                    text.Append(CultureInfo.InvariantCulture, $" xp:{Ids.Brief(k)} {v:0.#}");
                foreach (var (k, v) in b.Gained)
                    text.Append(CultureInfo.InvariantCulture, $" item:{Ids.Brief(k)} {v}");
                foreach (var (k, v) in b.Currency)
                    text.Append(CultureInfo.InvariantCulture, $" currency:{Ids.Brief(k)} {v}");
                foreach (var (k, v) in b.Used)
                    text.Append(CultureInfo.InvariantCulture, $" used:{k} {v}");
                foreach (var (k, v) in b.RareEvents)
                    text.Append(CultureInfo.InvariantCulture, $" rare:{Ids.Brief(k)} {v}");
                text.Append(FightText.BatchSuffix(b));
            }
            if (r.Fights.Count > 0)
            {
                text.Append("\nfights per stage:");
                foreach (var stage in r.Fights.GroupBy(f => f.Stage).OrderBy(g => g.Key))
                {
                    var deaths = stage.Count(f => f.Outcome == FightOutcome.Death);
                    var enemies = string.Join(
                        ", ",
                        stage.GroupBy(f => f.Enemy).Select(g => $"{Ids.Brief(g.Key)} ×{g.Count()}")
                    );
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\n  stage {stage.Key}: {stage.Count()} fights, {deaths} deaths, mean {stage.Average(f => f.GameSeconds):0.0} s ({enemies})"
                    );
                }
            }
        }
        if (RiskLine(r, "risk") is { } risk)
            text.Append('\n').Append(risk);
        if (full)
            text.Append('\n').Append(FightText.Block(r, names));
        if (r.Kind == TargetKind.Dungeon && r.HasEstimates)
            text.Append('\n').Append(FightsTable(r, names, "fights"));
        if (RareDropsBlock(r, unit, prices, names) is { } rare)
            text.Append('\n').Append(rare);
        return text.ToString();
    }

    /// <summary>The target's rare drops, one per line after the rule they follow; null when it has none.</summary>
    private static string? RareDropsBlock(TargetResult r, TimeUnit unit, PriceTable prices, Func<string, string> names)
    {
        var rows = RareDrops.Rows(r, unit, prices);
        if (rows.Count == 0)
            return null;
        var per = TimeUnits.Name(unit);
        var labels = rows.Select(row => names(row.Id)).ToList();
        var width = labels.Max(l => l.Length) + 2;
        var text = new StringBuilder(
            r.Kind == TargetKind.Dungeon
                ? "rare drops (rewards below 1% per completion, rare events):"
                : "rare drops (loot below 1% per kill, rare events):"
        );
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var source = row.Source switch
            {
                DropSource.Loot => "loot",
                DropSource.Reward => "reward",
                _ => "event",
            };
            var chance = row.Chance is { } c
                ? $"{Fmt.N(c * 100)}% per {(row.Per == ChancePer.MinuteInCombat ? "minute in combat" : row.Per == ChancePer.Completion ? "completion" : "kill")}"
                : "chance not given";
            text.Append("\n  ").Append(labels[i].PadRight(width)).Append(source.PadRight(7)).Append(chance);
            if (row.Expected is { } e)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"   {Fmt.N(e.Point)}/{per} ({Fmt.N(e.Low)}–{Fmt.N(e.High)})"
                );
            if (row.ObservedRate is { } seen)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"   observed {Fmt.N(seen.Point)}/{per} ({Fmt.N(seen.Low)}–{Fmt.N(seen.High)}), {row.Observed} seen"
                );
            else
                text.Append(CultureInfo.InvariantCulture, $"   observed {row.Observed}");
            if (row.Possible && row.ForOne is { } one)
                text.Append("   ").Append(ForOneText(one));
            if (row.ObservedOnly)
                text.Append("   not in the game's list: observed only");
            if (!row.Possible)
                text.Append("   not possible: ").Append(row.Unmet.English(" | "));
        }
        return text.ToString();
    }

    // "first drop 831 kills, 12.2 h (11.9–12.6 h); first one: half by 576, 9 in 10 by 1,912": hours, and rare events
    // in minutes in combat.
    private static string ForOneText(ForOne one)
    {
        var unit = one.Per switch
        {
            ChancePer.Completion => "completions",
            ChancePer.MinuteInCombat => "min in combat",
            _ => "kills",
        };
        var text = new StringBuilder("first drop");
        if (one.Each is { } each)
            text.Append(CultureInfo.InvariantCulture, $" {Fmt.N(each)} {unit}");
        if (one.Time is { } t)
            text.Append(
                CultureInfo.InvariantCulture,
                $"{(one.Each != null ? "," : "")} {Fmt.N(t.Point / 3600)} h ({Fmt.N(t.Low / 3600)}–{Fmt.N(t.High / 3600)} h)"
            );
        text.Append(
            CultureInfo.InvariantCulture,
            $"; first one: half by {Fmt.N(Math.Ceiling(one.Half))}, 9 in 10 by {Fmt.N(Math.Ceiling(one.NineInTen))} {unit}"
        );
        return text.ToString();
    }

    /// <summary>
    /// The risk read-out on one line, <paramref name="label"/> first; "not recorded" for results saved before risk
    /// was kept; null for a result without estimates.
    /// </summary>
    public static string? RiskLine(TargetResult r, string label)
    {
        if (Risk.Of(r) is not { } risk)
            return null;
        if (!risk.Recorded)
            return label + ": not recorded";
        var lowest = risk.LowestHp is { } hp ? Fmt.I(hp) : "every fight died";
        var calls = risk.CloseCalls!;
        // A close call is a fight won: with none won there is no share, and "0%" would read as safe.
        var closeCalls =
            risk.LowestHp == null
                ? "close calls: no fight survived"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"close calls {calls.Point * 100:0.0}% ({calls.Low * 100:0.0}–{calls.High * 100:0.0}%)"
                );
        var line = $"{label}: highest hit {Fmt.I(risk.HighestHit)}  lowest HP {lowest}  {closeCalls}";
        return risk.Warning ? line + "  WARNING: highest hit reaches lowest HP" : line;
    }

    /// <summary>
    /// A dungeon's stages as a table under <paramref name="label"/>, then the time between fights; highest and lowest
    /// are blank for fights recorded before they were kept.
    /// </summary>
    public static string FightsTable(TargetResult r, Func<string, string> names, string label)
    {
        var breakdown = StageBreakdown.Of(r, r.Stages);
        var enemies = breakdown
            .Rows.Select(row =>
                row.Reached == 0
                    ? "not reached"
                    : string.Join(", ", row.Enemies.Select(e => $"{names(e.Enemy)} ×{Fmt.I(e.Count)}"))
            )
            .ToList();
        var width = Math.Max("enemies".Length, enemies.Count == 0 ? 0 : enemies.Max(e => e.Length)) + 2;
        var indent = new string(' ', label.Length + 2);
        var text = new StringBuilder(
            $"{label}: {"stage", -7}{"enemies".PadRight(width)}{"reached", -9}{"mean s", -8}{"damage", -8}{"deaths", -8}{"highest", -9}{"lowest", -8}{"atk", -7}{"hit", -7}{"dealt", -8}{"e-atk", -7}{"e-hit", -7}ran-over"
        );
        for (var i = 0; i < breakdown.Rows.Count; i++)
        {
            var row = breakdown.Rows[i];
            var line = new StringBuilder(indent)
                .Append(CultureInfo.InvariantCulture, $"{row.Stage, -7}")
                .Append(enemies[i].PadRight(width))
                .Append(CultureInfo.InvariantCulture, $"{row.Reached, -9}")
                .Append(Column(row.MeanSeconds, "0.0", 8))
                .Append(Column(row.MeanDamage, "0.#", 8))
                .Append(CultureInfo.InvariantCulture, $"{row.Deaths, -8}")
                .Append(Column(row.MaxHit, "0", 9))
                .Append((row.MinHp is { } low ? Fmt.I(low) : "").PadRight(8))
                .Append(Column(row.Counts?.Attacks, "0.#", 7, "-"))
                .Append(Column(row.Counts?.Hits, "0.#", 7, "-"))
                .Append(Column(row.Counts?.Dealt, "0.#", 8, "-"))
                .Append(Column(row.Counts?.EnemyAttacks, "0.#", 7, "-"))
                .Append(Column(row.Counts?.EnemyHits, "0.#", 7, "-"))
                .Append(CultureInfo.InvariantCulture, $"{row.RanOver}");
            text.Append('\n').Append(line.ToString().TrimEnd());
        }
        if (breakdown.BetweenFightsSeconds is { } between)
            text.Append(CultureInfo.InvariantCulture, $"\nbetween fights: {between:0.0} s per completion");
        return text.ToString();
    }

    private static string Column(double? value, string format, int width, string missing = "") =>
        (value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : missing).PadRight(width);

    private static string Column(long? value, string format, int width) => Column((double?)value, format, width);

    public static string Name<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();
}
