using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// "Fight Details" in a target's details: the player's and the enemy's chance to hit, damage per hit and per
/// second, then what a kill cost, each with its 95% range, all from what the game reported in the fights. An
/// unfinished target shows its one trial as plain values; a run saved before counts were kept says so.
/// </summary>
internal static class FightBlock
{
    public static Control Make(TargetResult result, Func<string, string> names)
    {
        var block = Ui.Column(6, "Cs2FightStats");
        var head = Ui.Add(block, Ui.Row(8));
        // Translators: Heading of a section in a target's details: chance to hit, damage and what a kill cost, measured in the fights.
        var title = Ui.Add(head, Ui.Label(Tr.Get("Fight Details"), TextStyle.Heading));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.MouseFilter = Control.MouseFilterEnum.Stop;
        title.TooltipText = Tr.Get(
            "Measured over the trials: hits over attacks as they happened, so chance to hit can differ from the combat screen's."
        );
        if (!result.HasEstimates && result.Stall is { } stall)
            Unfinished(block, stall);
        else if (!result.HasFightCounts)
            // Translators: Shown in the fights section for a run saved before fight counts were kept; the second message explains it.
            Note(block, Tr.Get("Not recorded"), Tr.Get("Runs from before this version have no fight counts."));
        else
            Finished(block, result, names);
        return block;
    }

    private static void Finished(VBoxContainer block, TargetResult result, Func<string, string> names)
    {
        var dungeon = result.Kind == TargetKind.Dungeon;
        // Translators: Sub-heading in the fights section: the numbers for the player's attacks.
        var you = Group(block, "Cs2FightsYou", Tr.Get("You"));
        // Translators: Label in the fights section: hits over attacks as they happened. "It made no attack" replaces the value when there was no attack.
        Line(you, Tr.Get("Chance to hit"), Estimate(result, FightNumbers.Hit, true), Tr.Get("It made no attack"));
        // Translators: Label in the fights section: health removed per landed hit. "No hit landed" replaces the value when none landed.
        Line(you, Tr.Get("Damage per hit"), Estimate(result, FightNumbers.PerHit, false), Tr.Get("No hit landed"));
        // Translators: Label in the fights section: health removed per second of fighting, from every cause.
        Line(you, Tr.Get("Damage per second"), Estimate(result, FightNumbers.Dps, false), null);
        Causes(you, result, taken: false);
        // Translators: Sub-heading in the fights section: the numbers for the enemy's attacks on the player ("Enemies" for a dungeon).
        var enemy = Group(block, "Cs2FightsEnemy", dungeon ? Tr.Get("Enemies") : Tr.Get("Enemy"));
        Line(
            enemy,
            Tr.Get("Chance to hit"),
            Estimate(result, FightNumbers.EnemyHit, true),
            Tr.Get("It made no attack")
        );
        Line(
            enemy,
            Tr.Get("Damage per hit"),
            Estimate(result, FightNumbers.EnemyPerHit, false),
            Tr.Get("No hit landed")
        );
        // Translators: Label in the fights section: the player's health lost per second of fighting.
        Line(enemy, Tr.Get("Damage taken per second"), Estimate(result, FightNumbers.Taken, false), null);
        Causes(enemy, result, taken: true);
        // Translators: Sub-heading in the fights section: what one kill (one dungeon clear) cost.
        var kill = Group(block, "Cs2FightsKill", dungeon ? Tr.Get("Per clear") : Tr.Get("Per kill"));
        var time = Metrics.Estimate(result, Metrics.KillTime, TimeUnit.Hour);
        // Translators: Label: game time per kill (per dungeon clear). "No kill" replaces the value when nothing was killed.
        Line(
            kill,
            dungeon ? Tr.Get("Clear time") : Tr.Get("Time to kill"),
            time is { } t && double.IsFinite(t.Point)
                ? (Words.Seconds(Ui.Number(t.Point)), Ui.Range(t.Low, t.High))
                : null,
            Tr.Get("No kill")
        );
        if (FightNumbers.Estimate(result, FightNumbers.AttacksPerKill) is { } attacks)
        {
            var missed = FightNumbers.Estimate(result, FightNumbers.MissedPerKill);
            // Translators: Label and value: attacks made per kill (per clear), and how many of them missed; {0} and {1} are numbers.
            Line(
                kill,
                dungeon ? Tr.Get("Attacks per clear") : Tr.Get("Attacks per kill"),
                (
                    Tr.Format("{0}, of which {1} missed", [Ui.Number(attacks.Point), Ui.Number(missed?.Point ?? 0)]),
                    null
                ),
                null
            );
        }
        // Translators: Label in the fights section: the share of damage beyond the enemy's remaining health.
        Line(kill, Tr.Get("Overkill"), Estimate(result, FightNumbers.Overkill, true), null);
        if (result.RanOver > 0 && FightNumbers.Estimate(result, FightNumbers.RanOver) is { } share)
        {
            var box = Ui.Add(block, new PanelContainer { Name = "Cs2FightsRanOver" });
            PixelBox.Panel(
                box,
                new StyleBoxFlat
                {
                    BgColor = Look.RanOverBack(share.Point),
                    BorderColor = Look.RanOverColour(share.Point),
                    BorderWidthBottom = 1,
                    BorderWidthTop = 1,
                    BorderWidthLeft = 1,
                    BorderWidthRight = 1,
                    ContentMarginLeft = 10,
                    ContentMarginRight = 10,
                    ContentMarginTop = 6,
                    ContentMarginBottom = 6,
                }
            );
            var lines = Ui.Add(box, Ui.Column(4));
            var trials = result.Batches.Sum(b => b.Trials);
            var minutes = result.Stall is { } s ? Math.Round(s.LimitSeconds / 60, 1) : 0;
            lines.AddChild(
                Ui.Label(
                    // Translators: In a box in the fights section: how many trials passed the time limit; {2} is the limit in minutes.
                    Tr.Format("{0} of {1} trials ran over {2} minutes", [result.RanOver, Ui.Number(trials), minutes]),
                    TextStyle.Label,
                    wrap: true
                )
            );
            var why = result.Stall is { } stall ? " " + Tr.Note(StallReasons.Note(stall, Enemy(stall, names))) : "";
            lines.AddChild(
                Ui.Label(
                    // Translators: Under the line above: the share of trials that ran over, then its 95% range; the reason sentence follows.
                    Tr.Format("{0}, range {1}.", [Ui.Percent(share.Point), Percents(share.Low, share.High)]) + why,
                    TextStyle.Small,
                    wrap: true
                )
            );
        }
    }

    private static void Unfinished(VBoxContainer block, Stall stall)
    {
        var note = Ui.Add(block, Ui.Label("", TextStyle.Gold, "Cs2FightsNote"));
        // Translators: Shown above plain numbers for a target whose trial ran over the time limit before any finished.
        note.Text = Tr.Plural("{0} unfinished trial", "{0} unfinished trials", stall.Trials, stall.Trials);
        var numbers = FightNumbers.OfStall(stall);
        (string, string?)? Plain(string key, bool percent) =>
            numbers.TryGetValue(key, out var v) ? (percent ? Ui.Percent(v) : Ui.Number(v), null) : null;
        var you = Group(block, "Cs2FightsYou", Tr.Get("You"));
        Line(you, Tr.Get("Chance to hit"), Plain(FightNumbers.Hit, true), Tr.Get("It made no attack"));
        Line(you, Tr.Get("Damage per hit"), Plain(FightNumbers.PerHit, false), Tr.Get("No hit landed"));
        Line(you, Tr.Get("Damage per second"), Plain(FightNumbers.Dps, false), null);
        var enemy = Group(block, "Cs2FightsEnemy", Tr.Get("Enemy"));
        Line(enemy, Tr.Get("Chance to hit"), Plain(FightNumbers.EnemyHit, true), Tr.Get("It made no attack"));
        Line(enemy, Tr.Get("Damage per hit"), Plain(FightNumbers.EnemyPerHit, false), Tr.Get("No hit landed"));
        Line(enemy, Tr.Get("Damage taken per second"), Plain(FightNumbers.Taken, false), null);
    }

    private static void Note(VBoxContainer block, string title, string body)
    {
        block.AddChild(Ui.Label(title, TextStyle.Label, "Cs2FightsNote"));
        block.AddChild(Ui.Label(body, TextStyle.Small, wrap: true));
    }

    private static VBoxContainer Group(VBoxContainer block, string name, string title)
    {
        var group = Ui.Add(block, Ui.Column(2, name));
        group.AddChild(Ui.Label(title, TextStyle.Gold));
        return group;
    }

    // One number: its label, its value and range, or what there was nothing to measure in words.
    private static void Line(VBoxContainer group, string label, (string Value, string? Range)? shown, string? none)
    {
        var row = Ui.Add(group, Ui.Row(8));
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.TooltipText = Hint(label);
        // The name wraps rather than widen the details: in a narrow column a long name and value pass its edge.
        var name = Ui.Add(row, Ui.Label(label, TextStyle.Small, wrap: true));
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.VerticalAlignment = VerticalAlignment.Center;
        if (shown is not { } value)
        {
            row.AddChild(Ui.Label(none ?? "–", TextStyle.Small));
            return;
        }
        row.AddChild(Ui.Label(value.Value, TextStyle.BodyStrong));
        if (value.Range != null)
            row.AddChild(Ui.Label(value.Range, TextStyle.Tiny));
    }

    // What a number means, on its row's hover; the range after a value is the 95% range.
    private static string Hint(string label)
    {
        if (label == Tr.Get("Chance to hit"))
            // Translators: Hover of "Chance to hit" in Fight Details.
            return Tr.Get("Attacks that landed, of all attacks made. Can differ from the combat screen's chance.");
        if (label == Tr.Get("Damage per hit"))
            // Translators: Hover of "Damage per hit" in Fight Details.
            return Tr.Get("Health an attack removed when it landed, after armour and resistance.");
        if (label == Tr.Get("Damage per second") || label == Tr.Get("Damage taken per second"))
            // Translators: Hover of the two damage-per-second rows in Fight Details.
            return Tr.Get(
                "Health removed per second of fighting, from attacks, effects and reflect. Waiting for a spawn doesn't count."
            );
        if (label == Tr.Get("Overkill"))
            // Translators: Hover of "Overkill" in Fight Details.
            return Tr.Get("Damage beyond the enemy's remaining health, as a share of all damage dealt.");
        if (label == Tr.Get("Attacks per kill") || label == Tr.Get("Attacks per clear"))
            // Translators: Hover of "Attacks per kill" in Fight Details.
            return Tr.Get("Attacks made for each kill, and how many of them missed.");
        // Translators: Hover of the other rows in Fight Details.
        return Tr.Get("Measured over the trials; the small numbers are the 95% range.");
    }

    private static (string, string?)? Estimate(TargetResult result, string key, bool percent)
    {
        if (FightNumbers.Estimate(result, key) is not { } e)
            return null;
        return percent ? (Ui.Percent(e.Point), Percents(e.Low, e.High)) : (Ui.Number(e.Point), Ui.Range(e.Low, e.High));
    }

    private static string Percents(double low, double high) =>
        Tr.Format("{0} – {1}", [Ui.Percent(low), Ui.Percent(high)]);

    // The line under a damage per second that has effects or reflect in it.
    private static void Causes(VBoxContainer group, TargetResult result, bool taken)
    {
        var effects = FightNumbers.PerSecond(result, s => s.Effect, taken);
        var reflect = FightNumbers.PerSecond(result, s => s.Reflect, taken);
        // Translators: Line under a damage per second: the part from combat effects (damage over time) and from reflected damage, per second.
        var text =
            effects != null && reflect != null
                ? Tr.Format(
                    "of which effects {0}/s, reflect {1}/s",
                    [Ui.Number(effects.Point), Ui.Number(reflect.Point)]
                )
            : effects != null ? Tr.Format("of which effects {0}/s", Ui.Number(effects.Point))
            : reflect != null ? Tr.Format("of which reflect {0}/s", Ui.Number(reflect.Point))
            : null;
        if (text != null)
            group.AddChild(Ui.Label(text, TextStyle.Tiny, "Cs2FightsOther"));
    }

    private static string? Enemy(Stall stall, Func<string, string> names) => stall.Enemy is { } id ? names(id) : null;

    /// <summary>The block's lines for <c>cs2 ui</c>: "fights &lt;key&gt; …", "not recorded" or "1 unfinished trial".</summary>
    public static string Describe(TargetResult result)
    {
        if (!result.HasEstimates && result.Stall is { } stall)
            return $"fights {stall.Trials} unfinished trial{(stall.Trials == 1 ? "" : "s")}";
        if (!result.HasFightCounts)
            return "fights not recorded";
        var parts = FightNumbers
            .Keys.Select(k => (k, e: FightNumbers.Estimate(result, k)))
            .Where(p => p.e != null)
            .Select(p =>
                $"{p.k[FightNumbers.Prefix.Length..]}={p.e!.Point.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"
            );
        return "fights " + string.Join(" ", parts);
    }
}
