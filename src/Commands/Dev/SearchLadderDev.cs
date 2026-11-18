using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Search;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// <c>cs2dev search ladder</c>: a round's sets each fought at every count of a ladder with one seed and no drops,
/// through the search's own pool, so that ways of racing them can be replayed and scored without fighting again.
/// <c>arms</c> replays them, <c>prefix</c> checks that a longer fight's first batches hold a shorter fight's trials,
/// <c>batches</c> prints one result's batches with the set's hit chance against the target.
/// </summary>
internal static class SearchLadderDev
{
    private sealed class Ladder
    {
        public required Goal Goal { get; init; }
        public required double Share { get; init; }
        public required PriceTable Prices { get; init; }
        public required List<Candidate> Candidates { get; init; }
        public required List<int> Counts { get; init; }
        public int Truth { get; init; }
        public ConcurrentDictionary<int, ConcurrentDictionary<int, StageResult>> Results { get; } = new();
        public Dictionary<int, double>? HitChance { get; set; }
        public IReadOnlyList<string?>? Keys { get; set; }
        public double Seconds { get; set; }
        public string State { get; set; } = "starting";
    }

    private static Ladder? _ladder;

    public static void Run(CommandCall call, CommandArgs args)
    {
        switch (args.Word(0))
        {
            case "ladder":
                Start(call, args);
                break;
            case "laddered":
                call.Ok(_ladder == null ? "no ladder yet" : Status(_ladder));
                break;
            case "arms":
                call.Ok(Arms(Done(), args));
                break;
            case "prefix":
                call.Ok(Prefix(Done()));
                break;
            case "key":
                var at = int.Parse(args.Word(1) ?? "0", CultureInfo.InvariantCulture);
                call.Ok(Done().Keys is { } all && at < all.Count ? all[at] ?? "null" : "no keys");
                break;
            case "batches":
                call.Ok(Batches(Done(), args));
                break;
            default:
                throw new CommandException("usage: " + SearchDev.Sub.Usage);
        }
    }

    /// <summary>
    /// The setup and the sets of a <c>fight</c> or <c>ladder</c>: change lists separated by <c>|</c>, after the held
    /// set (index 0). <c>a.&lt;change&gt;</c> changes are held under every set, as a climb holds its steps so far.
    /// </summary>
    public static (Setup Setup, List<Candidate> Candidates) SetsFrom(CommandArgs args)
    {
        var game = LiveGame.Require();
        var sets = new List<List<Change>> { new() };
        var held = new List<Change>();
        foreach (var raw in args.Raw)
        {
            if (raw == "|")
                sets.Add([]);
            else if (
                raw.StartsWith("a.", StringComparison.Ordinal) && Change.TryParse(raw[2..], out var heldChange, out _)
            )
                held.Add(Changes.Resolve(game, heldChange!, raw[2..]));
            else if (raw.Contains('=', StringComparison.Ordinal) && Change.TryParse(raw, out var change, out _))
                sets[^1].Add(Changes.Resolve(game, change!, raw));
        }
        var candidates = new List<Candidate> { new(0, ChangeList.Empty, "") };
        foreach (var set in sets.Where(s => s.Count > 0))
            candidates.Add(new Candidate(candidates.Count, ChangeList.Of(set), ""));
        var setup = RunArgs.SetupFrom(args);
        if (held.Count > 0)
            setup.Changes = ChangeList.Of(held);
        return (setup, candidates);
    }

    private static Ladder Done() =>
        _ladder is { State: "done" } ladder
            ? ladder
            : throw new CommandException(_ladder == null ? "no ladder yet" : "the ladder is " + _ladder.State);

    private static void Start(CommandCall call, CommandArgs args)
    {
        if (_ladder is { State: not ("done" or "failed") })
            throw new CommandException("a ladder is still fighting (cs2dev search laddered)");
        var game = LiveGame.Require();
        var target = Targets.Resolve(game, args.Word(1) ?? throw new CommandException("usage: " + SearchDev.Sub.Usage));
        var (setup, candidates) = SetsFrom(args);
        var counts = (args.String("counts") ?? "100,400,1000")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => int.Parse(c, CultureInfo.InvariantCulture))
            .Distinct()
            .Order()
            .ToList();
        var seed = args.ULong("seed") ?? 1;
        // The truth's fights take a seed of their own, so they don't share their first trials with the race's.
        var truth = args.Int("truth", 0, 0, 100_000);
        var truthSeed = args.ULong("truthseed") ?? seed + 7919;
        if (truth > 0 && !counts.Contains(truth))
            counts = [.. counts.Append(truth).Order()];
        var copies = args.Int("copies", RunArgs.DefaultCopies(), 1, Math.Max(1, Environment.ProcessorCount));
        var slot = RunService.CheckLoadout(setup, () => LiveGame.Loadouts(game));
        var snapshot = LiveGame.TakeSnapshot(game);
        var ladder = new Ladder
        {
            Goal = new Goal(args.String("metric") ?? Metrics.MainXp),
            Share = RunService.DefaultTieShare(),
            Prices = LiveGame.Prices(game),
            Candidates = candidates,
            Counts = counts,
            Truth = truth,
        };
        _ladder = ladder;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Fight(int at)
        {
            if (at == counts.Count)
            {
                ladder.Seconds = clock.Elapsed.TotalSeconds;
                ladder.State = "done";
                return;
            }
            var count = counts[at];
            ladder.State = $"fighting {count} trials";
            var found = ladder.Results.GetOrAdd(count, _ => new());
            var pool = new SearchPool();
            pool.Start(
                new SearchJob(
                    snapshot,
                    slot,
                    setup.Changes,
                    target,
                    new DriveOptions(count, new TrialLimits(), setup.IgnoreRequirements),
                    count == truth ? truthSeed : seed,
                    copies,
                    candidates
                ),
                new SearchDev.Exhausting(
                    (index, result, skill) =>
                        found[index] = new StageResult(index, result, Answers.MetricFor(ladder.Goal, skill)),
                    () =>
                    {
                        pool.Dispose();
                        Fight(at + 1);
                    },
                    e =>
                    {
                        pool.Dispose();
                        ladder.State = $"failed: {e.GetType().Name}: {e.Message}";
                    }
                ),
                CancellationToken.None
            );
        }
        // Each set's stats are read on a check copy of its own with its changes made before the target spawns: changed
        // after it, the player's numbers against the target read as none until combat ticks again.
        ladder.State = "reading hit chances";
        var keys = new string?[candidates.Count];
        void ReadKey(int at)
        {
            if (at == candidates.Count)
            {
                ladder.Keys = keys;
                ladder.HitChance = HitChances(keys);
                Fight(0);
                return;
            }
            var changes = setup.Changes ?? ChangeList.Empty;
            foreach (var change in candidates[at].Changes)
                changes = changes.With(change);
            ChangeCheck.Read<IReadOnlyList<string?>>(
                snapshot,
                slot,
                changes,
                setup.IgnoreRequirements,
                copy =>
                    Game.Search.TargetStatsKey.Read(
                        copy,
                        target,
                        Game.Search.GearCatalog.Read(copy, target),
                        [new Candidate(0, ChangeList.Empty, "")],
                        setup.IgnoreRequirements,
                        CancellationToken.None
                    ) ?? [],
                (read, _) =>
                {
                    keys[at] = read is { Count: > 0 } ? read[0] : null;
                    ReadKey(at + 1);
                }
            );
        }
        ReadKey(0);
        call.Ok(
            $"ladder of {Ids.BriefKey(target.Key)} started: {candidates.Count} sets at {string.Join(", ", counts)} trials, seed {seed} (cs2dev search laddered)"
        );
    }

    // The player's chance to hit the target, the first "hit=" of a stats key (the player's numbers against the
    // enemy come before the enemy's against the player).
    private static Dictionary<int, double> HitChances(string?[] keys)
    {
        var chances = new Dictionary<int, double>();
        for (var i = 0; i < keys.Length; i++)
        {
            if (keys[i] is not { } key)
                continue;
            var at = key.IndexOf(";hit=", StringComparison.Ordinal);
            var none = key.IndexOf(";none;", StringComparison.Ordinal);
            if (at < 0 || (none >= 0 && none < at))
                continue;
            var end = key.IndexOf(';', at + 5);
            chances[i] = double.Parse(key[(at + 5)..end], CultureInfo.InvariantCulture);
        }
        return chances;
    }

    private static string Status(Ladder ladder) =>
        $"{ladder.State}: {ladder.Candidates.Count} sets; "
        + string.Join(", ", ladder.Counts.Select(c => $"{c}: {ladder.Results.GetValueOrDefault(c)?.Count ?? 0}"))
        + (ladder.State == "done" ? FormattableString.Invariant($"; {ladder.Seconds:0.0} s") : "");

    private static Dictionary<int, IReadOnlyDictionary<int, StageResult>> Frozen(Ladder ladder) =>
        ladder.Results.ToDictionary(
            r => r.Key,
            r => (IReadOnlyDictionary<int, StageResult>)new Dictionary<int, StageResult>(r.Value)
        );

    // cs2dev search arms [trials=1000] [truth=5000] [checks=a,b,c]: one JSON line per arm, then the truth.
    private static string Arms(Ladder ladder, CommandArgs args)
    {
        var trials = args.Int("trials", 1000, 1, 100_000);
        var arms = new LadderArms(Frozen(ladder), ladder.Goal, ladder.Share, ladder.Prices);
        var stageChecks = Answers.Stages(trials, ladder.Candidates.Count);
        var checks = args.String("checks") is { } given
            ? given.Split(',').Select(c => int.Parse(c, CultureInfo.InvariantCulture)).ToList()
            : [.. stageChecks];
        var at = ladder.Results[trials];
        var lines = new List<object>
        {
            new
            {
                Arm = "values",
                Values = at.ToDictionary(
                    r => r.Key,
                    r => Race.Value(r.Value, ladder.Prices)?.Point is { } v && double.IsFinite(v) ? v : (double?)null
                ),
                Deaths = at.ToDictionary(r => r.Key, r => r.Value.Result.Batches.Sum(b => b.Deaths)),
            },
            arms.Stages(trials),
            arms.FoughtOnce("once-held", trials, checks, leaderSteps: false),
            arms.FoughtOnce("once-held-leader-at-end", trials, checks, leaderSteps: true),
            arms.FoughtOnce("once-held-widened", trials, checks, leaderSteps: true, args.Double("widen", 1, 10) ?? 1),
        };
        if (args.Int("truth", 0, 0, 100_000) is var truth and > 0 && ladder.Results.ContainsKey(truth))
        {
            var end = arms.Truth(truth);
            lines.Add(
                new
                {
                    Arm = "truth",
                    Trials = truth,
                    Percent = end.Percent is { } p ? new[] { p.Point, p.Low, p.High } : null,
                    State = end.State.ToString(),
                    end.Best,
                    Ties = end.Ties.Select(t => t.Index).ToList(),
                }
            );
        }
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return string.Join("\n", lines.Select(l => JsonSerializer.Serialize(l, l.GetType(), options)));
    }

    // Each count below the largest against the largest fight's first batches: equal when those batches hold
    // exactly the shorter fight's trials, and every count summed agrees.
    private static string Prefix(Ladder ladder)
    {
        var raced = ladder.Counts.Where(c => c != ladder.Truth).ToList();
        var longest = raced[^1];
        var text = new StringBuilder();
        var checkedCount = 0;
        var skipped = new SortedSet<int>();
        foreach (var count in raced.SkipLast(1))
        foreach (var (index, shorter) in ladder.Results[count].OrderBy(r => r.Key))
        {
            if (!ladder.Results[longest].TryGetValue(index, out var full))
                continue;
            var batches = full.Result.Batches;
            var held = 0;
            var take = 0;
            while (take < batches.Count && held < count)
                held += batches[take++].Trials;
            // A count that ends inside a batch of the longer fight can't be read from its batches: skipped.
            if (held != count)
            {
                skipped.Add(count);
                continue;
            }
            checkedCount++;
            var a = Sums(batches.Take(take));
            var b = Sums(shorter.Result.Batches);
            if (a != b)
                text.Append(CultureInfo.InvariantCulture, $"\n{index} at {count}: {a} against {b}");
        }
        var notOnBoundary = skipped.Count == 0 ? "" : $", {string.Join(", ", skipped)} not on a batch boundary";
        return (text.Length == 0 ? $"prefix ok: {checkedCount} checks{notOnBoundary}" : $"prefix differs:") + text;
    }

    private static string Sums(IEnumerable<Tally> batches)
    {
        var list = batches.ToList();
        var dealt = new SideCounts();
        foreach (var b in list.Where(b => b.Fight != null))
            dealt.Add(b.Fight!.Dealt);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"trials {list.Sum(b => b.Trials)} kills {list.Sum(b => b.Kills)} deaths {list.Sum(b => b.Deaths)} s {list.Sum(b => b.GameSeconds):0.###} xp {list.Sum(b => b.Xp.Values.Sum()):0.###} attacks {dealt.Attacks} hits {dealt.Hits} damage {dealt.Attack}"
        );
    }

    // cs2dev search batches <count> [set=…]: per batch, trials, kills, deaths, game seconds, the goal's numerator
    // and denominator, and the player's attacks, hits and attack damage; with the set's hit chance where read.
    private static string Batches(Ladder ladder, CommandArgs args)
    {
        var count = int.Parse(
            args.Word(1) ?? throw new CommandException("usage: cs2dev search batches <count>"),
            CultureInfo.InvariantCulture
        );
        if (!ladder.Results.TryGetValue(count, out var sets))
            throw new CommandException($"no fights of {count} trials in the ladder");
        var text = new StringBuilder();
        foreach (var (index, set) in sets.OrderBy(s => s.Key))
        {
            var hit = ladder.HitChance?.GetValueOrDefault(index, double.NaN) ?? double.NaN;
            text.Append(
                CultureInfo.InvariantCulture,
                $"set {index} {set.Metric} hit {(double.IsNaN(hit) ? "-" : hit.ToString("0.######", CultureInfo.InvariantCulture))}\n"
            );
            var series = Metrics.Series(set.Result, set.Metric, TimeUnit.Hour, ladder.Prices);
            for (var i = 0; i < set.Result.Batches.Count; i++)
            {
                var b = set.Result.Batches[i];
                var dealt = b.Fight?.Dealt;
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"  {i} {b.Trials} {b.Kills} {b.Deaths} {b.GameSeconds:0.###} {(series is { } s ? s.Values[i] : double.NaN):0.######} {(series is { } d ? d.Denominators[i] : double.NaN):0.######} {dealt?.Attacks ?? -1} {dealt?.Hits ?? -1} {dealt?.Attack ?? -1}\n"
                );
            }
        }
        return text.ToString().TrimEnd();
    }
}
