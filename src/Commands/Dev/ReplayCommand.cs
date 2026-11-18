using System.Globalization;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Dev;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// <c>cs2dev replay</c>: a run or search of an unpacked bug report fought again from its own snapshot and request,
/// and whether it came out as the report says.
/// </summary>
internal static class ReplayCommand
{
    // The bridge stops a command after 10 s: a replay that takes longer is waited for with cs2dev replay wait.
    private const int MaxWaitSeconds = 9;

    public static Subcommand Sub { get; } =
        new(
            "replay",
            "cs2dev replay <folder> [run=<id>|search=<id>] [in=host|game] [copies=<n>] [anyway] | cs2dev replay wait [timeout=<s>] | cs2dev replay cancel",
            ["run", "search", "in", "copies", "timeout"],
            ["anyway"],
            Run,
            Purpose: "Rerun a report's run or search from its own snapshot and request, and say whether it matched"
        );

    private static void Run(CommandCall call, CommandArgs args)
    {
        switch (args.Word(0))
        {
            case "wait":
                Wait(call, args.Double("timeout", 0, MaxWaitSeconds) ?? MaxWaitSeconds);
                return;
            case "cancel":
                (
                    Replay.Last is { Done: false } active ? active : throw new CommandException("no replay is running")
                ).Cancel();
                call.Ok("replay cancelled");
                return;
            case null:
                throw new CommandException("usage: " + Sub.Usage);
        }
        var where = args.String("in");
        if (where is not (null or RunService.InHost or RunService.InGame))
            throw new CommandException("in=host|game");
        var options = new ReplayOptions(
            where,
            args.String("copies") is null ? null : args.Int("copies", 1, 1, 64),
            args.Flag("anyway")
        );
        var report = ReportFolder.Open(args.Word(0)!);
        if (args.String("search") is { } searchId || (args.String("run") is null && report.PickRun(null) is null))
            ReplaySearch.Start(report, report.PickSearch(args.String("search")), options);
        else
            Replay.Start(report, report.PickRun(args.String("run"))!, options, Send);
        Wait(call, MaxWaitSeconds, started: true);
    }

    private static bool Send(string lever) =>
        lever == "hang" ? HostClient.Instance.Hang() : HostClient.Instance.Lever(lever);

    // Replies with the outcome once the replay has ended; while it lasts, says what is under way.
    private static void Wait(CommandCall call, double timeout, bool started = false)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        MainThread.When(
            () => Replays.Done,
            () => call.Ok(string.Join("\n", Replays.Lines())),
            TimeSpan.FromSeconds(timeout),
            () =>
            {
                var what = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Replays.Running} after {watch.Elapsed.TotalSeconds:0.0} s: cs2dev replay wait"
                );
                if (started)
                    call.Ok("still replaying " + what);
                else
                    call.Error("still replaying " + what);
            }
        );
    }

    // The last replay of a run or of a search.
    private static class Replays
    {
        public static bool Done => !Replay.IsActive && !ReplaySearch.IsActive;

        public static IReadOnlyList<string> Lines() =>
            ReplaySearch.LastStarted > Replay.LastStarted
                ? ReplaySearch.Last?.Lines() ?? []
                : Replay.Last?.Lines() ?? ["no replay yet"];

        public static string Running =>
            Replay.Last?.Current is { } run ? $"{run.Id} {run.Done}/{run.Targets.Count} targets" : "the search";
    }
}
