using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// The run options shared by the run, compare and what-if commands: targets, setup, trials, copies and where.
/// </summary>
internal static class RunArgs
{
    /// <summary>The targets the panel's filter shows, less the ignored ones, for <c>cs2 run selection</c>.</summary>
    public static Func<IReadOnlyList<TargetSpec>>? Selection { get; set; }

    private static int DefaultTrials { get; set; } = 1000;

    public static Func<int> DefaultCopies { get; set; } = () => Engine.DefaultCopies.Now;

    /// <summary>
    /// The targets named by <paramref name="words"/>: keys, <c>current</c>, <c>selection</c> or <c>all</c>.
    /// </summary>
    private static List<TargetSpec> TargetsFrom(IEnumerable<string> words)
    {
        var game = LiveGame.Require();
        var targets = new List<TargetSpec>();
        foreach (var word in words)
        {
            switch (word.ToLowerInvariant())
            {
                case "all":
                    targets.AddRange(Targets.All(game));
                    break;
                case "current":
                    var current =
                        LiveGame.Current(game) ?? throw new CommandException("not fighting: nothing is current");
                    targets.Add(TargetSpec.FromKey(current.Key));
                    break;
                case "selection":
                    var selection =
                        Selection?.Invoke()
                        ?? throw new CommandException("no selection: open the panel to choose targets");
                    targets.AddRange(selection);
                    break;
                default:
                    targets.Add(Targets.Resolve(game, word));
                    break;
            }
        }
        return [.. targets.DistinctBy(t => t.Key)];
    }

    /// <summary>A run request from the shared run options (trials, seed, copies, where, fresh, stop).</summary>
    public static RunRequest RequestFrom(CommandArgs args, Setup setup, IEnumerable<string> targetWords) =>
        new(
            setup,
            TargetsFrom(targetWords),
            args.Int("trials", DefaultTrials, 1, 100_000),
            args.ULong("seed"),
            args.Int("copies", DefaultCopies(), 1, Math.Max(1, Environment.ProcessorCount)),
            Where(args),
            Fresh: args.Flag("fresh"),
            StopPrecision: args.Double("stop", 0.5, 50) / 100
        );

    /// <summary>Reads <c>where=host|game</c>, or null for the setting.</summary>
    public static string? Where(CommandArgs args) =>
        args.String("where")?.ToLowerInvariant() switch
        {
            null => null,
            RunService.InHost => RunService.InHost,
            RunService.InGame => RunService.InGame,
            var other => throw new CommandException($"where={other}: use where=host or where=game"),
        };

    public static Setup SetupFrom(CommandArgs args) => SetupOf(args.String("setup"), args.Flag("ignore-req"));

    /// <summary>A setup from a slot id (<c>current</c> or none for the character as is).</summary>
    public static Setup SetupOf(string? slot, bool ignoreRequirements) =>
        new()
        {
            Loadout =
                slot == null || slot.Equals(Setup.Current, StringComparison.OrdinalIgnoreCase)
                    ? Setup.Current
                    : Ids.Full(slot),
            IgnoreRequirements = ignoreRequirements,
        };
}
