using System.Diagnostics;
using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// <c>cs2 stats</c> and <c>cs2 modifiers</c>: a setup's stats and modifiers as a run would fight with it, read from a
/// check copy (nothing runs; the live game is untouched). With <c>b=</c> or changes, side B beside side A; a change
/// goes to side B unless it names side A.
/// </summary>
internal static class Cs2SetupCommands
{
    // The bridge stops a command after 10 s; a sheet read takes a copy build at most.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(9);

    public static void Add(List<Subcommand> subs) =>
        subs.AddRange([
            new(
                "stats",
                "cs2 stats [setup] [b=<setup>] [change…] [ignore-req]",
                ["b", .. ChangeArgs.Options],
                ["ignore-req"],
                Stats
            ),
            new(
                "modifiers",
                "cs2 modifiers [setup] [filter] [b=<setup>] [change…] [ignore-req]",
                ["b", .. ChangeArgs.Options],
                ["ignore-req"],
                Modifiers
            ),
        ]);

    private static void Stats(CommandCall call, CommandArgs args)
    {
        var (sides, _) = Sides(args);
        Read(call, sides, sheets => StatsText(sheets));
    }

    private static void Modifiers(CommandCall call, CommandArgs args)
    {
        var (sides, filter) = Sides(args);
        Read(call, sides, sheets => ModifiersText(sheets, filter));
    }

    // Side A from the first word when it names a setup, side B from b= and its changes; the other words are a filter.
    private static (List<Setup> Sides, string? Filter) Sides(CommandArgs args)
    {
        var game = LiveGame.Require();
        var ignore = args.Flag("ignore-req");
        string? slot = null;
        var words = args.Words.ToList();
        if (words.Count > 0 && IsSetup(LiveGame.Loadouts(game), words[0]))
        {
            slot = words[0];
            words.RemoveAt(0);
        }
        var (changesA, changesB) = ChangeArgs.From(args, SideLetter.B);
        var a = RunArgs.SetupOf(slot, ignore);
        a.Changes = changesA;
        var sides = new List<Setup> { a };
        if (args.String("b") is { } b || !changesB.IsEmpty)
        {
            var other = RunArgs.SetupOf(args.String("b") ?? slot, ignore);
            other.Changes = changesB;
            sides.Add(other);
        }
        return (sides, words.Count > 0 ? string.Join(' ', words) : null);
    }

    private static bool IsSetup(IReadOnlyList<LoadoutInfo> loadouts, string word) =>
        word.Equals(Setup.Current, StringComparison.OrdinalIgnoreCase) || loadouts.Any(l => l.Slot == Ids.Full(word));

    private static void Read(CommandCall call, List<Setup> sides, Func<IReadOnlyList<SetupSheet>, string> text)
    {
        var watch = Stopwatch.StartNew();
        MainThreadReply.ReplyWhen<IReadOnlyList<SetupSheet>>(
            call,
            done => RunService.ReadSetup(sides, done),
            (sheets, error) =>
            {
                if (sheets == null)
                {
                    call.Error($"the check copy failed: {error?.GetType().Name}: {error?.Message}");
                    return;
                }
                call.Ok(
                    text(sheets) + string.Create(CultureInfo.InvariantCulture, $"\nelapsed={watch.ElapsedMilliseconds}")
                );
            },
            Timeout,
            $"the check copy took longer than {Timeout.TotalSeconds:0} s"
        );
    }

    private static string Label(Setup setup)
    {
        var label = setup.Loadout == Setup.Current ? Setup.Current : Ids.Brief(setup.Loadout);
        if (setup.IgnoreRequirements)
            label += " ignore-req";
        return setup.Changes is { } changes ? $"{label} + {changes.ShortForm}" : label;
    }

    private static readonly (StatGroup Group, string Name)[] Groups =
    [
        (StatGroup.Levels, "levels"),
        (StatGroup.Offence, "offence"),
        (StatGroup.Defence, "defence"),
        (StatGroup.Other, "other"),
        (StatGroup.Totals, "totals"),
    ];

    private static string StatsText(IReadOnlyList<SetupSheet> sheets)
    {
        var a = sheets[0];
        if (sheets.Count == 1)
        {
            var one = new StringBuilder($"stats {Label(a.Setup)} (build {a.GameBuild})");
            foreach (var (group, name) in Groups)
            {
                var lines = a.Stats.Where(s => s.Group == group).ToList();
                if (lines.Count == 0)
                    continue;
                one.Append('\n').Append((name + ":").PadRight(10));
                one.Append(string.Join("  ", lines.Select(s => $"{s.Name.English()} {s.Text}")));
            }
            return one.ToString();
        }
        var b = sheets[1];
        var diff = SheetDiff.Of(a, b);
        var text = new StringBuilder($"stats A={Label(a.Setup)} B={Label(b.Setup)} (build {a.GameBuild})");
        text.Append(
            CultureInfo.InvariantCulture,
            $"\ndiffering {diff.Stats.Count(s => s.Differs)} of {diff.Stats.Count}"
        );
        foreach (var (group, name) in Groups)
        {
            var lines = diff.Stats.Where(s => (s.A ?? s.B)!.Group == group).ToList();
            if (lines.Count == 0)
                continue;
            text.Append('\n').Append(name).Append(':');
            foreach (var s in lines)
            {
                var label = (s.A ?? s.B)!.Name.English();
                var delta =
                    s.A != null && s.B != null && SheetDiff.TextDelta(s.A.Text, s.B.Text) is { } written ? written
                    : s.Delta is { } d && d != 0 ? (d > 0 ? "+" : "") + Fmt.N(d)
                    : "";
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"\n{(s.Differs ? "*" : " ")} {label}  {s.A?.Text ?? "—"}  {s.B?.Text ?? "—"}  {delta}"
                );
            }
        }
        return text.ToString();
    }

    private static readonly Dictionary<ModifierGroup, string> GroupNames = new()
    {
        [ModifierGroup.Equipment] = "equipment",
        [ModifierGroup.SetBonus] = "set bonuses",
        [ModifierGroup.Prayer] = "prayers",
        [ModifierGroup.Toolbelt] = "potions and toolbelt",
        [ModifierGroup.Food] = "food",
        [ModifierGroup.StanceSpell] = "stance and spell",
        [ModifierGroup.Pet] = "pets",
        [ModifierGroup.Shop] = "shop",
        [ModifierGroup.Mastery] = "mastery",
        [ModifierGroup.Agility] = "agility",
        [ModifierGroup.Astrology] = "astrology",
        [ModifierGroup.Area] = "area",
        [ModifierGroup.Other] = "other",
    };

    private static string ModifiersText(IReadOnlyList<SetupSheet> sheets, string? filter)
    {
        var a = sheets[0];
        var two = sheets.Count > 1;
        var sides = two
            ? SheetDiff.Of(a, sheets[1]).Modifiers
            : [.. a.Modifiers.Select(m => new ModifierSide(m, true, false))];
        var shown = sides.Where(s => ModifierSearch.Matches(s.Line, filter)).ToList();
        var sources = sides.Select(s => (s.Line.Group, s.Line.SourceId)).Distinct().Count();
        var label = two ? $"A={Label(a.Setup)} B={Label(sheets[1].Setup)}" : Label(a.Setup);
        var text = new StringBuilder($"modifiers {label}: {sides.Count} from {sources} sources");
        if (filter != null)
            text.Append(CultureInfo.InvariantCulture, $" (filter \"{filter}\": {shown.Count})");
        foreach (var group in shown.GroupBy(s => s.Line.Group).OrderBy(g => g.Key))
        {
            text.Append('\n').Append(GroupNames[group.Key]);
            foreach (var side in group)
            {
                var line = side.Line;
                var mark = two && side.OneSide ? "* " : "  ";
                text.Append('\n').Append(mark).Append(line.SourceName).Append(": ").Append(line.Description);
                if (line.OnEnemy)
                    text.Append(" (on the enemy)");
                if (two)
                    text.Append("  ")
                        .Append(
                            side.InA && side.InB ? "A B"
                            : side.InA ? "A"
                            : "B"
                        );
            }
        }
        return text.ToString();
    }
}
