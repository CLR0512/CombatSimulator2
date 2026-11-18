using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// <c>cs2dev search keys</c>: how long reading each set's stats key takes, and whether reading a plan's sets by
/// stepping from one to the next gives the keys reading each from current gear gives.
/// </summary>
internal static class SearchKeysDev
{
    /// <summary>How the last plan's keys came out.</summary>
    public static string Result => _result;

    // How long reading each set's stats key takes, by part: cs2dev search keys <target> <change…> [| <change…>]….
    public static void OfSets(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var target = Targets.Resolve(
            game,
            args.Word(1) ?? throw new CommandException("usage: cs2dev search keys <target> <change…> [| <change…>]…")
        );
        var sets = new List<List<Change>> { new() };
        foreach (var raw in args.Raw)
        {
            if (raw == "|")
                sets.Add([]);
            else if (raw.Contains('=', StringComparison.Ordinal) && Change.TryParse(raw, out var change, out _))
                sets[^1].Add(Changes.Resolve(game, change!, raw));
        }
        var candidates = new List<Candidate> { new(0, ChangeList.Empty, "") };
        foreach (var set in sets.Where(s => s.Count > 0))
            candidates.Add(new Candidate(candidates.Count, ChangeList.Of(set), ""));
        var setup = RunArgs.SetupFrom(args);
        var slot = RunService.CheckLoadout(setup, () => LiveGame.Loadouts(game));
        MainThreadReply.ReplyWhen<string>(
            call,
            done =>
                ChangeCheck.Read(
                    LiveGame.TakeSnapshot(game),
                    slot,
                    null,
                    false,
                    copy =>
                    {
                        var times = new List<string>();
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        var catalogue = Game.Search.GearCatalog.Read(copy, target);
                        var read = clock.Elapsed.TotalMilliseconds;
                        Game.Search.TargetStatsKey.Read(
                            copy,
                            target,
                            catalogue,
                            candidates,
                            false,
                            CancellationToken.None,
                            times
                        );
                        return FormattableString.Invariant(
                                $"catalogue {read:0} ms, keys {clock.Elapsed.TotalMilliseconds - read:0} ms\n"
                            ) + string.Join("\n", times);
                    },
                    done
                ),
            (text, error) =>
            {
                if (text == null)
                    call.Error(error?.Message ?? "no answer");
                else
                    call.Ok(text);
            },
            TimeSpan.FromSeconds(9),
            "the probe took too long"
        );
    }

    // The keys of every set a search would fight, read stepping from set to set and again each from current gear,
    // with both times and the sets whose keys differ: cs2dev search keys <target> plan [lock=] [metric=] [<change>…];
    // it can take longer than a reply waits, so cs2dev search keys result tells how it ended.
    private static volatile string _result = "no keys read yet (cs2dev search keys <target> plan)";

    public static void OfPlan(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var target = Targets.Resolve(game, args.Word(1)!);
        var setup = RunArgs.SetupFrom(args);
        var changes = ChangeArgs.From(args, SideLetter.A).A;
        var slot = RunService.CheckLoadout(setup, () => LiveGame.Loadouts(game));
        var request = new SearchRequest(
            SearchKind.BestGear,
            setup,
            target.Key,
            new Goal(args.String("metric") ?? Metrics.MainXp),
            1000,
            1
        )
        {
            Locks = (args.String("lock") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Ids.Full)
                .ToHashSet(StringComparer.Ordinal),
        };
        _result = "reading";
        ChangeCheck.Read(
            LiveGame.TakeSnapshot(game),
            slot,
            changes,
            setup.IgnoreRequirements,
            copy =>
            {
                var catalogue = Game.Search.GearCatalog.Read(copy, target);
                var plan = Planner.Plan(catalogue, request);
                if (plan.Climb)
                    return $"too many sets to fight them all ({plan.Possible})";
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var stepped = Game.Search.TargetStatsKey.Read(
                    copy,
                    target,
                    catalogue,
                    plan.Candidates,
                    false,
                    CancellationToken.None
                );
                var steppedMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                var fresh = Game.Search.TargetStatsKey.Read(
                    copy,
                    target,
                    catalogue,
                    plan.Candidates,
                    false,
                    CancellationToken.None,
                    stepwise: false
                );
                var freshMs = clock.Elapsed.TotalMilliseconds;
                if (stepped == null || fresh == null)
                    return "no keys: a dungeon, or the enemy didn't spawn";
                var differ = Enumerable
                    .Range(0, plan.Candidates.Count)
                    .Where(i => stepped[i] != null && fresh[i] != null && stepped[i] != fresh[i])
                    .ToList();
                var text = new StringBuilder(
                    FormattableString.Invariant(
                        $"{plan.Candidates.Count} sets: stepped {steppedMs:0} ms ({stepped.Count(k => k != null)} read), each from current gear {freshMs:0} ms ({fresh.Count(k => k != null)} read), {differ.Count} differ"
                    )
                );
                foreach (var i in differ.Take(3))
                {
                    var x = stepped[i]!.Split(';', '\n');
                    var y = fresh[i]!.Split(';', '\n');
                    var parts = x.Zip(y)
                        .Where(p => p.First != p.Second)
                        .Take(4)
                        .Select(p => p.First + " / " + p.Second);
                    text.Append("\n  ")
                        .Append(plan.Candidates[i].IsCurrent ? "current gear" : plan.Candidates[i].Changes.ShortForm)
                        .Append(": ")
                        .Append(string.Join("; ", parts));
                }
                return text.ToString();
            },
            (text, error) => _result = text ?? $"error {error?.GetType().Name}: {error?.Message}"
        );
        call.Ok("reading the keys (cs2dev search keys result)");
    }
}
