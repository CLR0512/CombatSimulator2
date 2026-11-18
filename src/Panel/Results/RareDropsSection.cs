using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The "Rare drops" section: loot (or a dungeon's rewards) below 1% per kill with the game's chance and the expected
/// rate, and the game's rare events with their chance per minute in combat and the observed rate; "Not possible" with
/// the game's reason when the setup can't get one.
/// </summary>
internal static class RareDropsSection
{
    public static Disclosure? Make(TargetResult result, TimeUnit unit, Func<string, string> names)
    {
        var game = LiveGame.Find();
        Func<string, Texture2D?> art = key => game != null ? Art.Metric(game, key) : Art.Missing;
        var rows = RareDrops.Rows(result, unit, PanelState.Prices());
        if (rows.Count == 0)
            return null;
        var loot = rows.Where(r => r.Source != DropSource.RareEvent).ToList();
        var events = rows.Where(r => r.Source == DropSource.RareEvent).ToList();
        var dungeon = result.Kind == TargetKind.Dungeon;
        var summary = string.Join(
            " · ",
            new[]
            {
                loot.Count > 0
                    ? (
                        dungeon
                            ? Tr.Plural("{0} reward", "{0} rewards", loot.Count, loot.Count)
                            : Tr.Format("{0} loot", loot.Count)
                    )
                    : null,
                events.Count > 0 ? Tr.Plural("{0} event", "{0} events", events.Count, events.Count) : null,
            }.OfType<string>()
        );
        var section = new Disclosure(
            "Cs2RareDrops",
            Tr.Get("Rare drops"),
            summary,
            PanelState.Prefs.RareDropsOpen,
            open =>
            {
                PanelState.Prefs.RareDropsOpen = open;
                PanelState.Save();
            }
        );
        var body = section.Body;
        body.AddChild(
            Ui.Label(
                dungeon
                    ? Tr.Get("Rewards below 1% per completion, and rare events")
                    : Tr.Get("Loot below 1% per kill, and rare events"),
                TextStyle.Small,
                wrap: true
            )
        );
        var index = 0;
        if (loot.Count > 0)
        {
            var kills = Metrics.Estimate(result, Metrics.Kills, unit);
            var per = Words.PerUnitShort(unit);
            body.AddChild(
                Ui.Label(
                    dungeon
                        ? Tr.Format(
                            "Rewards · chance per completion · {0} from {1} completions {2}",
                            [Words.PerUnit(unit), Ui.Number(kills?.Point ?? 0), per]
                        )
                        : Tr.Format(
                            "Loot · chance per kill · {0} from {1} kills {2}",
                            [Words.PerUnit(unit), Ui.Number(kills?.Point ?? 0), per]
                        ),
                    TextStyle.Tiny,
                    wrap: true
                )
            );
            foreach (var row in loot)
                body.AddChild(Row(row, unit, art, names, index++));
        }
        if (events.Count > 0)
        {
            body.AddChild(
                Ui.Label(
                    Tr.Get("Rare events · chance per minute in combat · rate as observed"),
                    TextStyle.Tiny,
                    wrap: true
                )
            );
            foreach (var row in events)
                body.AddChild(Row(row, unit, art, names, index++));
        }
        return section;
    }

    private static PanelContainer Row(
        RareDropRow row,
        TimeUnit unit,
        Func<string, Texture2D?> art,
        Func<string, string> names,
        int index
    )
    {
        var box = Look.Panel(Art.RowBox, Ui.Id("Cs2RareDrop", index.ToString(CultureInfo.InvariantCulture)));
        var line = Ui.Add(box, Ui.Row(8));
        var icon = art((row.Source == DropSource.RareEvent ? "rare:" : "item:") + row.Id);
        line.AddChild(Slot.Make(icon ?? Art.Missing, 32));
        var who = Ui.Add(line, Ui.Column(0));
        who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        who.AddChild(Ui.Clipped(names(row.Id), TextStyle.Small));
        var chance =
            row.Chance is not { } c ? Tr.Get("observed only: not in the game's list")
            : row.Per == ChancePer.MinuteInCombat ? Tr.Format("{0} per minute in combat", Words.Chance(c))
            : row.Per == ChancePer.Completion ? Tr.Format("{0} per completion", Words.Chance(c))
            : Tr.Format("{0} per kill", Words.Chance(c));
        who.AddChild(Ui.Label(chance, TextStyle.Tiny));
        if (row.Possible && row.ForOne is { } one)
        {
            who.AddChild(
                Ui.Label(
                    OneLine(one),
                    TextStyle.Tiny,
                    Ui.Id("Cs2RareDropForOne", index.ToString(CultureInfo.InvariantCulture))
                )
            );
            box.TooltipText = FirstOne(one);
        }
        if (!row.Possible)
        {
            var why = Ui.Label(Tr.Notes(row.Unmet, "; "), TextStyle.Tiny, wrap: true);
            why.Modulate = Look.Deadly;
            who.AddChild(why);
        }
        var right = Ui.Add(line, Ui.Column(0));
        if (!row.Possible)
        {
            right.AddChild(Ui.Label(Tr.Get("Not possible"), TextStyle.Small));
            return box;
        }
        var rate = row.Expected ?? row.ObservedRate;
        var per = Words.PerUnitShort(unit);
        if (rate != null)
        {
            right.AddChild(Right(Tr.Format("{0} {1}", [Rate(rate.Point), per]), TextStyle.Small));
            right.AddChild(Right(Tr.Format("{0} – {1}", [Rate(rate.Low), Rate(rate.High)]), TextStyle.Tiny));
        }
        right.AddChild(Right(Tr.Format("observed {0}", Ui.Number(row.Observed)), TextStyle.Tiny));
        return box;
    }

    // "First drop ≈831 kills · ≈12.2 h": what the first takes on average; a rare event only has its time in combat.
    private static string OneLine(ForOne one)
    {
        var time = one.Time is { } t ? Words.Duration(t.Point) : null;
        if (one.Each is not { } each)
            // Translators: How long the first rare event takes on average: {0} a time, "16.7 h".
            return Tr.Format("First ≈{0} in combat", time ?? "");
        var count = Ui.Number(Math.Round(each));
        var per =
            one.Per == ChancePer.Completion
                // Translators: How many completions the first rare reward takes on average: {0} a count.
                ? Tr.Format("First drop ≈{0} completions", count)
                // Translators: How many kills the first rare drop takes on average: {0} a count, "831".
                : Tr.Format("First drop ≈{0} kills", count);
        return time == null ? per : Tr.Format("{0} · ≈{1}", [per, time]);
    }

    // The tooltip, a line each: the first drop is luck, so its 50% and 90% marks say more than the average.
    // "First drop, by luck:" / "50% chance within 576 kills (8.4 h)" / "90% chance within 1,912 kills (1.2 days)"
    private static string FirstOne(ForOne one)
    {
        string Within(double tries, double? seconds)
        {
            var count = Ui.Number(Math.Ceiling(tries));
            if (one.Per == ChancePer.MinuteInCombat)
                // Translators: Rare drop tooltip, a rare event: {0} time in combat, "8.4 h".
                return Tr.Format("{0} in combat", Words.Duration(tries * 60));
            var amount =
                one.Per == ChancePer.Completion ? Tr.Format("{0} completions", count) : Tr.Format("{0} kills", count);
            return seconds is { } s ? Tr.Format("{0} ({1})", [amount, Words.Duration(s)]) : amount;
        }
        return string.Join(
            "\n",
            // Translators: Rare drop tooltip, first line; the two lines after it give the chance of having it by then.
            Tr.Get("First drop, by luck:"),
            // Translators: Rare drop tooltip: {0} kills (or completions, or time in combat), "576 kills (8.4 h)".
            Tr.Format("50% chance within {0}", Within(one.Half, one.HalfTime)),
            // Translators: Rare drop tooltip: {0} kills (or completions, or time in combat), "1,912 kills (1.2 days)".
            Tr.Format("90% chance within {0}", Within(one.NineInTen, one.NineInTenTime))
        );
    }

    // Rare things come at small rates: keep their significant digits.
    private static string Rate(double value) =>
        value is > 0 and < 1 ? value.ToString(value < 0.01 ? "0.#####" : "0.####", Tr.Culture) : Ui.Number(value);

    private static Label Right(string text, TextStyle style)
    {
        var label = Ui.Label(text, style);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }
}
