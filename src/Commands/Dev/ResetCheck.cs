using System.Diagnostics;
using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.Core;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary><c>cs2dev reset</c>: fights targets in one copy and lists what leaks between them.</summary>
internal static class ResetCheck
{
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _resetLines = new();
    private static volatile bool _resetRunning;

    /// <summary>
    /// Fights targets one after another in one prepared copy (in this process), resetting it after each, and lists
    /// the fingerprint parts that differ from the prepared copy's after each reset, and a hash of each fight's
    /// batches: a target named twice gives the same hash both times unless something the reset doesn't put back
    /// changes its fight. Also times a lone build, a build beside a live copy, the prepare and a fingerprint.
    /// </summary>
    public static void Run(CommandCall call, CommandArgs args)
    {
        if (args.Word(0) is "result")
        {
            var text = new StringBuilder(_resetRunning ? "running" : "done");
            while (_resetLines.TryDequeue(out var line))
                text.Append('\n').Append(line);
            call.Ok(text.ToString());
            return;
        }
        if (_resetRunning)
            throw new CommandException("a reset check is running (cs2dev reset result)");
        var game = LiveGame.Require();
        if (args.Words.Count == 0)
            throw new CommandException("usage: cs2dev reset <target…|all> [setup=<loadout>] [ignore-req]");
        var targets = args.Words.Any(w => w.Equals("all", StringComparison.OrdinalIgnoreCase))
            ? Targets.All(game)
            : [.. args.Words.Select(w => Targets.Resolve(game, w))];
        var ignore = args.Flag("ignore-req");
        var options = new DriveOptions(args.Int("trials", 1000, 1, 100_000), new Model.TrialLimits(), ignore);
        var snapshot = LiveGame.TakeSnapshot(game);
        var loadout = Engine.RunService.CheckLoadout(RunArgs.SetupFrom(args), () => LiveGame.Loadouts(game));
        _resetLines.Clear();
        _resetRunning = true;
        var thread = new Thread(() =>
        {
            void Say(string line) => _resetLines.Enqueue(line);
            try
            {
                SharedCaches.Fill();
                var watch = Stopwatch.StartNew();
                using var copy = Copy.Build();
                var lone = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                using (Copy.Build()) { }
                var beside = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                copy.Prepare(snapshot, loadout);
                var prepare = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                copy.KeepPristine();
                var keep = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                var parts = copy.Parts().Count;
                var fp = watch.Elapsed.TotalMilliseconds;
                Say(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"build lone {lone:0} ms, beside a live copy {beside:0} ms, prepare {prepare:0} ms (apply {copy.ApplyMs:0}), keep {keep:0} ms, fingerprint {fp:0} ms over {parts} parts"
                    )
                );
                foreach (var target in targets)
                {
                    var eligibility = Targets.Check(copy.Game, target);
                    if (!eligibility.Eligible && !ignore)
                        continue;
                    var modsBefore = copy.ModifierEntries();
                    var guildsBefore = copy.Guilds();
                    var conditionsBefore = copy.Conditions.Summary();
                    var bankBefore = copy.BankStacks();
                    var extrasBefore = copy.BankExtras();
                    copy.Reseed(1, target.Key);
                    watch.Restart();
                    var result = Driver.Fight(copy, target, eligibility, options, CancellationToken.None);
                    var fight = watch.Elapsed.TotalMilliseconds;
                    watch.Restart();
                    var left = copy.Reset();
                    var reset = watch.Elapsed.TotalMilliseconds;
                    Say(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{Model.Ids.BriefKey(target.Key)} {ResultText.Name(result.State)} batches {Engine.Dev.FightLog.Hash(result)} fight {fight:0} ms reset {reset:0} ms: {(left.Count == 0 ? "clean" : string.Join(", ", left))}"
                        )
                    );
                    if (left.Count > 0)
                    {
                        var bankAfter = copy.BankStacks();
                        Say(
                            $"  bank +[{string.Join(" ", bankAfter.Except(bankBefore))}] -[{string.Join(" ", bankBefore.Except(bankAfter))}] order {(bankAfter.SequenceEqual(bankBefore) ? "same" : "differs: before " + string.Join(" ", bankBefore) + " | after " + string.Join(" ", bankAfter))}; before {extrasBefore}; after {copy.BankExtras()}"
                        );
                        Say($"  guilds before {guildsBefore} after {copy.Guilds()}");
                        Say($"  conditions before {conditionsBefore} after {copy.Conditions.Summary()}");
                        var modsAfter = copy.ModifierEntries();
                        foreach (var m in modsAfter.Except(modsBefore))
                            Say("  + " + m);
                        foreach (var m in modsBefore.Except(modsAfter))
                            Say("  - " + m);
                        break;
                    }
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Say($"error {e.GetType().Name}: {e.Message}");
                Log.Error($"cs2dev reset: {e}");
            }
            finally
            {
                _resetRunning = false;
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "cs2-reset-check",
        };
        thread.Start();
        call.Ok($"reset check started over {targets.Count} targets (cs2dev reset result)");
    }
}
