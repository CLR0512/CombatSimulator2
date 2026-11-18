using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary><c>cs2 blocks</c>: the blocks that help a master most, from its result, with no fights.</summary>
internal static class Cs2BlocksCommands
{
    public static void Add(List<Subcommand> subs) =>
        subs.Add(
            new(
                "blocks",
                "cs2 blocks <master> [metric=<key>] [run=<id>] [open]",
                ["metric", "run"],
                ["open"],
                Blocks,
                Purpose: "The blocks that help a master most, from its result in a run; open puts them on side B"
            )
        );

    private static void Blocks(CommandCall call, CommandArgs args)
    {
        var word = args.Word(0) ?? throw new CommandException("usage: cs2 blocks <master> [metric=] [run=] [open]");
        var key = Ids.FullKey(Ids.IsMaster(word) ? word : Ids.MasterPrefix + word);
        var run = RunFinder.Find(args.String("run"));
        if (run.Results.GetValueOrDefault(key) is not { } master)
            throw new CommandException($"{run.Id} has no {Ids.BriefKey(key)} (cs2 run {Ids.BriefKey(key)})");
        var game = LiveGame.Require();
        var metric = Metrics.Resolve(run, args.String("metric") ?? PanelState.Prefs.Metric);
        var suggestion =
            BlockSuggestions.For(master, metric, TimeUnit.Hour, LiveGame.Prices(game), Ranking.LowerIsBetter(metric))
            ?? throw new CommandException($"{Ids.BriefKey(key)} has no figures in {run.Id} to weigh blocks with");
        var text = BlocksText.Of(suggestion, master, run.Id, id => LiveGame.NameOf(game, id));
        if (args.Flag("open"))
            text += "\n" + Open(suggestion, master);
        call.Ok(text);
    }

    // The suggestion as a blocks change on side B, beside the changes side A already has.
    private static string Open(BlockSuggestion suggestion, TargetResult master)
    {
        var best = suggestion.Best ?? throw new CommandException("no suggestion to open");
        var blocks = best.Blocks.Select(k => k[(k.IndexOf('/', StringComparison.Ordinal) + 1)..]).ToList();
        var change = new Change(
            ChangeKind.Blocks,
            Ids.MasterOf(master.Key),
            blocks.Count == 0 ? Change.None : string.Join(',', blocks)
        );
        var a = PanelState.A;
        var b = new SideState(a.Base, null, a.IgnoreRequirements).WithChanges(a.Changes.With(change));
        PanelState.SetSide(SideLetter.B, b);
        return StateLines.SideLine(SideLetter.B, b);
    }
}
