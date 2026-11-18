using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.CombatSimulator2.Panel.Simulate;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// <c>cs2 filter</c>, <c>ignore</c>, <c>unignore</c> and <c>ignored</c>: which targets are shown and run, as the
/// panel's filters and its strip set them.
/// </summary>
internal static class Cs2TargetCommands
{
    public static void Add(List<Subcommand> subs) =>
        subs.AddRange([
            new(
                "filter",
                "cs2 filter [can-fight|all] [kinds=monsters,dungeons,slayer,masters|none] [areas=<id,…>|all] [search=<text>]",
                ["kinds", "areas", "search"],
                ["can-fight", "all"],
                Filter,
                Purpose: "Sets which targets are shown and run; with no argument prints the filter"
            ),
            new("ignore", "cs2 ignore <target…> | cs2 ignore area=<area>", ["area"], [], (c, a) => Ignore(c, a, true)),
            new(
                "unignore",
                "cs2 unignore <target…> | cs2 unignore area=<area> | cs2 unignore all",
                ["area"],
                ["all"],
                (c, a) => Ignore(c, a, false)
            ),
            new("ignored", "cs2 ignored", [], [], (call, _) => Reply(call, () => IgnoredText())),
        ]);

    private static void Filter(CommandCall call, CommandArgs args)
    {
        var filter = PanelState.Prefs.Filter;
        if (args.Flag("can-fight") && args.Flag("all"))
            throw new CommandException("give can-fight or all, not both");
        if (args.Flag("can-fight") || args.Flag("all"))
            filter = filter with { CanFightOnly = args.Flag("can-fight") };
        if (args.String("kinds") is { } kinds)
            filter = filter with { Kinds = Kinds(kinds) };
        if (args.String("areas") is { } areas)
            filter = filter with
            {
                Areas = areas.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : [.. areas.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Ids.Full)],
            };
        if (args.String("search") is { } search)
            filter = filter with { Search = search };
        PanelState.SetFilter(filter);
        Reply(call, () => StateLines.FilterLine() + "\n" + Counts());
    }

    private static TargetTags Kinds(string text)
    {
        var tags = TargetTags.None;
        foreach (var word in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            tags |= word.ToLowerInvariant() switch
            {
                "monsters" => TargetTags.Monsters,
                "dungeons" => TargetTags.Dungeons,
                "slayer" => TargetTags.Slayer,
                "masters" => TargetTags.Masters,
                "none" => TargetTags.None,
                _ => throw new CommandException($"kinds={text}: use monsters, dungeons, slayer, masters or none"),
            };
        return tags;
    }

    private static void Ignore(CommandCall call, CommandArgs args, bool ignore)
    {
        var game = LiveGame.Require();
        List<string> keys;
        if (!ignore && args.Flag("all"))
            keys = [.. PanelState.Ignored];
        else if (args.String("area") is { } area)
        {
            var id = Ids.Full(area);
            keys = [.. Targets.All(game).Where(t => t.Area == id).Select(t => t.Key)];
            if (keys.Count == 0)
                throw new CommandException($"no area '{area}' (see cs2 targets)");
        }
        else if (args.Words.Count > 0)
            keys = [.. args.Words.Select(word => Targets.Resolve(game, word).Key)];
        else
            throw new CommandException(
                ignore
                    ? "usage: cs2 ignore <target…> | area=<area>"
                    : "usage: cs2 unignore <target…> | area=<area> | all"
            );
        PanelState.SetIgnored(keys, ignore);
        Reply(call, () => IgnoredText());
    }

    private static string Counts() => StateLines.TargetsLine()!;

    // Every ignored target, shown by the filter or not, then the counts.
    private static string IgnoredText()
    {
        var text = new StringBuilder();
        foreach (var key in PanelState.Ignored.Order(StringComparer.Ordinal))
            text.Append("ignored ").Append(Ids.BriefKey(key)).Append('\n');
        return text.Append(Counts()).ToString();
    }

    // Replies once targets are checked for the panel's setup: the counts need to know what it can fight.
    private static void Reply(CommandCall call, Func<string> text)
    {
        var game = LiveGame.Require();
        var setup = SetupCheck.Current();
        setup.IgnoreRequirements = false; // a target's reasons don't depend on ignoring them
        setup.Character = LiveGame.CharacterName(game);
        if (PanelState.Catalog is { } cached && cached.Setup == setup.Identity)
        {
            call.Ok(text());
            return;
        }
        var slot = setup.Loadout == Setup.Current ? null : setup.Loadout;
        MainThreadReply.ReplyWhen<TargetCatalog>(
            call,
            done => TargetCatalog.Check(game, setup, slot, done),
            (catalog, error) =>
            {
                if (catalog == null)
                {
                    call.Error($"check failed: {error?.GetType().Name}: {error?.Message}");
                    return;
                }
                PanelState.Catalog = catalog;
                call.Ok(text());
            },
            TimeSpan.FromSeconds(9),
            "the check copy took longer than 9 s"
        );
    }
}
