using System.Globalization;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary><c>cs2 loadouts</c>: the character's saved loadouts, and Best loadouts on a master.</summary>
internal static class Cs2LoadoutsCommands
{
    private const string Usage =
        "cs2 loadouts | cs2 loadouts best master:<m>|all [masters=<m>,…] [side=a|b] [metric=<key>] [deaths=none|5|any] [seed=] [trials=] "
        + "[copies=] [where=host|game] | cs2 loadouts best status [all] | cs2 loadouts best use <master>|all | cs2 loadouts best cancel "
        + "| cs2 loadouts tasks [metric=<key>]";

    public static void Add(List<Subcommand> subs) =>
        subs.Add(
            new(
                "loadouts",
                Usage,
                ["masters", "side", "metric", "deaths", "seed", "trials", "copies", "where"],
                [],
                Loadouts,
                Purpose: "The character's loadouts; best picks a saved loadout per monster of a master and puts the picks on side B"
            )
        );

    private static void Loadouts(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        if (args.Word(0) == null)
        {
            var all = LiveGame.Loadouts(game);
            call.Ok(
                $"{all.Count} loadouts"
                    + string.Concat(
                        all.Select(l => $"\n  {Ids.Brief(l.Slot)}  {l.Name}  {(l.Unlocked ? "unlocked" : "locked")}")
                    )
            );
            return;
        }
        if (args.Word(0) == "tasks")
        {
            call.Ok(
                Tasks(
                    Game.Slayer.SlayerTasks.Current(game),
                    LiveGame.Prices(game),
                    args.String("metric") ?? "xp:MelvorBase:Slayer"
                )
            );
            return;
        }
        if (args.Word(0) != "best")
            throw new CommandException("usage: " + Usage);
        var service = RunService.Instance;
        switch (args.Word(1))
        {
            case "status" when args.Word(2) == "all":
                call.Ok(
                    StatusAll(
                        service.BestMasters ?? throw new CommandException("Best loadouts for every master hasn't run")
                    )
                );
                return;
            case "status":
                call.Ok(Status(service.BestLoadouts ?? throw new CommandException("Best loadouts hasn't run")));
                return;
            case "all":
                var all = service.StartBestMasters(SideSetup(args), GoalOf(args), Options(args, null), MastersOf(args));
                call.Ok($"Best loadouts started on {all.Masters.Count} masters (cs2 loadouts best status all)");
                return;
            case "use":
                var ran = service.BestMasters is { State: BestLoadoutsState.Done } done
                    ? done
                    : throw new CommandException("Best loadouts for every master hasn't finished");
                if (args.Word(2) == "all")
                {
                    Panel.Slayer.MonsterLoadouts.UseAll(ran);
                    call.Ok($"Best on side B for {ran.Done.Count(m => m.Figure != null)} masters");
                    return;
                }
                var which = args.Word(2) is { } name
                    ? Ids.FullKey(Ids.IsMaster(Ids.FullKey(name)) ? name : Ids.MasterPrefix + name)
                    : throw new CommandException("usage: cs2 loadouts best use <master>|all");
                var best =
                    ran.Done.FirstOrDefault(m => m.Master == which && m.Figure != null)
                    ?? throw new CommandException($"{Ids.BriefKey(which)} has no best setup in the last run");
                Panel.Slayer.MonsterLoadouts.Use(best);
                call.Ok($"Best on side B for {Ids.BriefKey(which)}");
                return;
            case "cancel":
                service.CancelBestLoadouts();
                call.Ok("Best loadouts cancelled: side B is as it was");
                return;
            case { } word when Ids.IsMaster(Ids.FullKey(word)) || Ids.IsMaster(Ids.FullKey(Ids.MasterPrefix + word)):
                var key = Ids.FullKey(Ids.IsMaster(Ids.FullKey(word)) ? word : Ids.MasterPrefix + word);
                var setup = SideSetup(args);
                var job = service.StartBestLoadouts(
                    key,
                    setup,
                    GoalOf(args),
                    Options(args, setup),
                    Panel.Slayer.MonsterLoadouts.PutPicks
                );
                call.Ok(
                    $"Best loadouts started on {Ids.BriefKey(key)}: {job.Choices.Count} choices, seed {job.Seed} (cs2 loadouts best status)"
                );
                return;
            default:
                throw new CommandException("usage: " + Usage);
        }
    }

    // The masters masters= names, as keys ("Kuren" or "master:Kuren"); null for every master.
    private static List<string>? MastersOf(CommandArgs args) =>
        args.String("masters")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => Ids.FullKey(Ids.IsMaster(Ids.FullKey(m)) ? m : Ids.MasterPrefix + m))
            .ToList();

    // The side Best loadouts starts from: side A unless side=b.
    private static Setup SideSetup(CommandArgs args) =>
        ((args.String("side") ?? "a") == "b" ? PanelState.B : PanelState.A)?.ToSetup("")
        ?? throw new CommandException("there is no side B: give side=a or open side B");

    private static Goal GoalOf(CommandArgs args) =>
        new(args.String("metric") ?? Metrics.MainXp, Cs2GearCommands.DeathsFrom(args.String("deaths")));

    private static RunRequest Options(CommandArgs args, Setup? setup) =>
        new(
            setup ?? new Setup(),
            [],
            args.Int("trials", 1000, 1, 100_000),
            args.ULong("seed"),
            args.Int("copies", RunArgs.DefaultCopies(), 1, Math.Max(1, Environment.ProcessorCount)),
            RunArgs.Where(args)
        );

    // Each master's task in the save with its verdict, from the current run's master results.
    private static string Tasks(IReadOnlyList<TaskNow> tasks, PriceTable prices, string metric)
    {
        return $"{tasks.Count} tasks · metric {metric}"
            + string.Concat(
                tasks.Select(t =>
                {
                    var head = $"\n  {Ids.Brief(t.Master)}  {Ids.Brief(t.Enemy)}  {t.Progress}/{t.Length}";
                    // As the Slayer page judges it: on the master's best setup once Find best has found it.
                    var source = Panel.SetupTab.SlayerTasksNow.Source(Ids.MasterKey(t.Master));
                    if (source is not { } s || TaskVerdicts.For(t, s.Result, metric, prices) is not { } v)
                        return head + "  not run";
                    var gain = v.Gain is { } g
                        ? string.Create(CultureInfo.InvariantCulture, $"  +{g:N0} over {v.Kills} kills for {v.Cost:N0}")
                        : "";
                    return head
                        + string.Create(
                            CultureInfo.InvariantCulture,
                            $"  {v.Advice.ToString().ToLowerInvariant()}  {v.Ratio:0.00}x"
                        )
                        + gain
                        + (s.Best ? "  on best" : "  on side a");
                })
            );
    }

    private static string StatusAll(BestMastersRun run) =>
        run.State switch
        {
            BestLoadoutsState.Running =>
                $"Best loadouts for every master: running, {run.Done.Count} of {run.Masters.Count} masters done, now {Ids.BriefKey(run.Current ?? "")}",
            BestLoadoutsState.Cancelled => "Best loadouts for every master: cancelled",
            BestLoadoutsState.Failed => $"Best loadouts for every master: {run.Error?.English()}",
            _ => LoadoutsText.Masters(run.Ranked, run.Goal),
        };

    private static string Status(BestLoadouts job) =>
        job.State switch
        {
            BestLoadoutsState.Running =>
                $"Best loadouts on {Ids.BriefKey(job.Master)}: running, {job.Results.Count} of {job.Choices.Count} choices done",
            BestLoadoutsState.Cancelled => $"Best loadouts on {Ids.BriefKey(job.Master)}: cancelled",
            BestLoadoutsState.Failed => $"Best loadouts on {Ids.BriefKey(job.Master)}: {job.Error?.English()}",
            _ => job.Picks is { } picks
                // By id: a reply stays the same whatever the player names a loadout.
                ? LoadoutsText.Of(picks, job.Seed, Ids.Brief, "B")
                : $"Best loadouts on {Ids.BriefKey(job.Master)}: no monsters to pick for",
        };
}
