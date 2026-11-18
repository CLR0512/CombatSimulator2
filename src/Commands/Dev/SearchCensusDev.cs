using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// <c>cs2dev search census</c>: every part of every wearable item in the game, owned or not, that the search has no
/// rule for, ordered by how many items a rule would free. Run after a game update to see what is worth a rule.
/// </summary>
internal static class SearchCensusDev
{
    /// <summary>The last census, or how it stands.</summary>
    public static string Result => _result;

    private static volatile string _result = "no census yet (cs2dev search census <target>)";

    // The target matters only for slayer-task bonuses, left out when it is no task.
    public static void Start(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var target = Targets.Resolve(
            game,
            args.Word(1) ?? throw new CommandException("usage: cs2dev search census <target>")
        );
        var setup = RunArgs.SetupFrom(args);
        var slot = RunService.CheckLoadout(setup, () => LiveGame.Loadouts(game));
        _result = "counting";
        ChangeCheck.Read(
            LiveGame.TakeSnapshot(game),
            slot,
            null,
            setup.IgnoreRequirements,
            copy => Game.Search.GearCatalog.Read(copy, target),
            (catalogue, error) =>
                _result = catalogue == null ? $"error {error?.GetType().Name}: {error?.Message}" : Describe(catalogue)
        );
        call.Ok("counting (cs2dev search censused)");
    }

    private static string Describe(GearCatalogue catalogue)
    {
        var (opaque, wearable) = Census.Totals(catalogue);
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"build {catalogue.Build}: {opaque} of {wearable} wearable items can't be compared ({catalogue.Items.Count(i => i.Opaque && i.Owned > 0)} owned)"
            )
        );
        foreach (var row in Census.Of(catalogue))
            text.Append(
                CultureInfo.InvariantCulture,
                $"\n{row.Part}: {row.Items} items ({row.Owned} owned), alone on {row.Alone}{(row.SetBonuses > 0 ? $", {row.SetBonuses} set bonuses" : "")}: {string.Join(", ", row.Names)}"
            );
        return text.ToString();
    }
}
