using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The "Fight by fight" section: a dungeon's stages from its fight records, each with the game's enemy portraits, how
/// often each enemy was rolled, the trials that reached it, mean time, damage, deaths, highest hit and lowest HP; then
/// the time between fights.
/// </summary>
internal static class FightsSection
{
    public static Disclosure Make(TargetResult result, Func<string, string> names, string? side = null)
    {
        var game = LiveGame.Find();
        Func<string, Texture2D?> portrait = id => game != null ? Art.Enemy(game, id) : Art.Missing;
        var breakdown = StageBreakdown.Of(result, result.Stages);
        var deathStages = breakdown.Rows.Where(r => r.Deaths > 0).Select(r => r.Stage).ToList();
        var summary = Tr.Plural("{0} stage", "{0} stages", breakdown.Rows.Count, breakdown.Rows.Count);
        if (deathStages.Count > 0)
            summary = Tr.Format(
                "{0} · deaths at stage {1}",
                [summary, string.Join(", ", deathStages.Select(s => s.ToString(Tr.Culture)))]
            );
        var title = side == null ? Tr.Get("Fight by fight") : Tr.Format("Fight by fight · {0}", side);
        var section = new Disclosure(
            side == null ? "Cs2Fights" : "Cs2Fights" + side,
            title,
            summary,
            PanelState.Prefs.FightsOpen,
            open =>
            {
                PanelState.Prefs.FightsOpen = open;
                PanelState.Save();
            }
        );
        var body = section.Body;
        var header = Ui.Add(body, Ui.Row(4));
        foreach (
            var caption in new[]
            {
                Tr.Get("Time"),
                Tr.Get("Damage"),
                Tr.Get("Deaths"),
                Tr.Get("Highest hit"),
                Tr.Get("Lowest HP"),
            }
        )
        {
            header.AddChild(Ui.Clipped(caption, TextStyle.Tiny));
        }
        var clear = breakdown.Rows.Sum(r => r.MeanSeconds ?? 0);
        var recorded = breakdown.Rows.Any(r => r.MaxHit != null);
        foreach (var row in breakdown.Rows)
            body.AddChild(Stage(row, portrait, names, clear, recorded));
        if (breakdown.BetweenFightsSeconds is { } between)
        {
            var line = Ui.Add(body, HpBar.Flow());
            line.AddChild(Ui.Label(Tr.Get("Time between fights"), TextStyle.Small));
            line.AddChild(
                Ui.Label(Tr.Format("{0} s per completion", between.ToString("0.0", Tr.Culture)), TextStyle.Small)
            );
        }
        var note =
            !recorded ? Tr.Get("Highest hit and lowest HP per fight: not recorded in this run.")
            : breakdown.Rows.Any(r => r.Deaths > 0) ? Tr.Get("Later fights count only the trials that reached them.")
            : Tr.Get("The bar is the stage's share of the clear time.");
        body.AddChild(Ui.Label(note, TextStyle.Tiny, wrap: true));
        return section;
    }

    private static PanelContainer Stage(
        StageRow row,
        Func<string, Texture2D?> art,
        Func<string, string> names,
        double clear,
        bool recorded
    )
    {
        var box = Look.Panel(Art.RowBox, Ui.Id("Cs2FightStage", row.Stage.ToString(CultureInfo.InvariantCulture)));
        if (row.Deaths > 0)
            PixelBox.Panel(
                box,
                new StyleBoxFlat
                {
                    BgColor = Colors.Black with { A = 0.25f },
                    BorderColor = Look.Deadly,
                    BorderWidthBottom = 1,
                    BorderWidthTop = 1,
                    BorderWidthLeft = 1,
                    BorderWidthRight = 1,
                    ContentMarginLeft = 6,
                    ContentMarginRight = 6,
                    ContentMarginTop = 4,
                    ContentMarginBottom = 4,
                }
            );
        var column = Ui.Add(box, Ui.Column(3));
        var top = Ui.Add(column, Ui.Row(6));
        top.AddChild(Ui.Label(row.Stage.ToString(Tr.Culture), TextStyle.Small));
        var first = row.Enemies.Count > 0 ? row.Enemies[0].Enemy : null;
        var portrait = Slot.Make(first != null ? art(first) : Art.Missing, 32);
        if (row.Reached == 0)
            portrait.Modulate = Colors.White with { A = 0.4f };
        top.AddChild(portrait);
        var who = Ui.Add(top, Ui.Column(0));
        who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var nameLine = Ui.Add(who, Ui.Row(4));
        nameLine.AddChild(
            Ui.Clipped(
                row.Enemies.Count == 0
                    ? Tr.Get("Not reached")
                    : string.Join(", ", row.Enemies.Select(e => names(e.Enemy))),
                TextStyle.Small
            )
        );
        if (row.MaxHit is { } hit && row.MinHp is { } low && hit >= low)
            nameLine.AddChild(RiskMark.Make("Cs2FightStageMark"));
        who.AddChild(
            Ui.Label(
                row.Enemies.Count == 0
                    ? Tr.Get("not rolled")
                    : string.Join(", ", row.Enemies.Select(e => Tr.Format("×{0} rolled", Ui.Number(e.Count)))),
                TextStyle.Tiny
            )
        );
        var reached = Ui.Add(top, Ui.Column(0));
        var count = Ui.Label(Ui.Number(row.Reached), TextStyle.Small);
        count.HorizontalAlignment = HorizontalAlignment.Right;
        reached.AddChild(count);
        var caption = Ui.Label(Tr.Get("reached"), TextStyle.Tiny);
        caption.HorizontalAlignment = HorizontalAlignment.Right;
        reached.AddChild(caption);
        if (row.Reached == 0)
        {
            column.AddChild(Ui.Label(Tr.Get("Not reached"), TextStyle.Tiny));
            return box;
        }
        var figures = Ui.Add(column, Ui.Row(4));
        Cell(figures, Tr.Format("{0} s", (row.MeanSeconds ?? 0).ToString("0.0", Tr.Culture)), null);
        Cell(figures, Ui.Number(row.MeanDamage ?? 0), null);
        Cell(figures, Ui.Number(row.Deaths), row.Deaths > 0 ? Look.Deadly : null);
        Cell(figures, recorded && row.MaxHit is { } h ? Ui.Number(h) : "—", null);
        Cell(
            figures,
            recorded && row.MinHp is { } l ? Ui.Number(l) : "—",
            row.MaxHit is { } mh && row.MinHp is { } ml && mh >= ml ? Look.Deadly : null
        );
        column.AddChild(Ui.Clipped(Counts(row), TextStyle.Tiny, "Cs2FightStageCounts"));
        var share = Bar.Make("", 4);
        Bar.Set(share, clear > 0 ? (row.MeanSeconds ?? 0) / clear : 0, Look.BarFill);
        column.AddChild(share);
        return box;
    }

    // The stage's attacks per fight, both ways, and what the player dealt; a dash in runs saved before they were kept.
    private static string Counts(StageRow row)
    {
        // Translators: Under a dungeon stage in "Fight by fight": mean per fight. {0}/{1} the player's hits/attacks,
        // {2} damage dealt, {3}/{4} the enemy's hits/attacks.
        var text = row.Counts is { } c
            ? Tr.Format(
                "You hit {0} / {1} · dealt {2} · it hit {3} / {4}",
                [
                    Ui.Number(c.Hits),
                    Ui.Number(c.Attacks),
                    Ui.Number(c.Dealt),
                    Ui.Number(c.EnemyHits),
                    Ui.Number(c.EnemyAttacks),
                ]
            )
            : "—";
        if (row.RanOver == 0)
            return text;
        // Translators: Added after the line above when fights at this stage passed the time limit; {0} is how many.
        return text
            + " · "
            + Tr.Plural("{0} fight ran over", "{0} fights ran over", row.RanOver, Ui.Number(row.RanOver));
    }

    // Cells share the row and shorten with "…" when the panel is narrow or the text large.
    private static void Cell(HBoxContainer row, string text, Color? colour)
    {
        var label = Ui.Clipped(text, TextStyle.Small);
        if (colour is { } c)
            label.Modulate = c;
        row.AddChild(label);
    }
}
