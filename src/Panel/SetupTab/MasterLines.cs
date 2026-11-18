using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>The lines of a master's row on the Slayer page that need no state of the page.</summary>
internal static class MasterLines
{
    // The best's blocks with the master's coin and their cost, and how many monsters fight on their own loadout; or
    // what it gets stuck on.
    public static Control Best(BestMaster best, Texture2D? coin, Func<string, string> names)
    {
        string Enemy(string key) => names(TargetSpec.FromKey(key).Enemy ?? key);
        if (best.Figure == null)
            return Ui.Label(
                best.Stuck is { } stuck
                    ? Tr.Format("Gets stuck on {0} with every loadout", Enemy(Ids.PartTarget(stuck)))
                    : Tr.Get("No figures"),
                TextStyle.Tiny,
                wrap: true
            );
        var row = Ui.Row(4);
        void Text(string text) => row.AddChild(Ui.Label(text, TextStyle.Tiny));
        var swaps = Tr.Plural("{0} on its own loadout", "{0} on their own loadouts", best.Swaps, best.Swaps);
        if (best.Blocked.Count == 0)
        {
            Text(Tr.Format("Best: {0} · {1}", [Tr.Get("no blocks"), swaps]));
            return row;
        }
        Text(Tr.Format("Best: blocks {0} ·", string.Join(", ", best.Blocked.Select(Enemy))));
        if (best.Block is { Cost: > 0 } block)
        {
            if (coin != null)
                row.AddChild(Ui.Picture(coin, 14));
            Text(
                Tr.Format(
                    "{0}{1} ·",
                    [
                        Ui.Number(block.Cost),
                        block.SlotsNeeded > 0 ? " · " + Tr.Format("needs slot {0}", block.Blocks.Count) : "",
                    ]
                )
            );
        }
        Text(swaps);
        return row;
    }

    // The best's figure, deaths and gain, then Use on B, or that B holds it already.
    // The figures' and the gain's columns, wide enough for "12,345" over "0.12 deaths / h" and "+123.4% vs A".
    private const float FiguresWidth = 130;
    private const float GainWidth = 130;
    private const float UseWidth = 100;

    public static void Figures(HBoxContainer line, BestMaster best)
    {
        // Fixed widths, right-aligned, centred in the row: the figures line up from master to master.
        var figures = Ui.Add(line, Ui.Column(0));
        figures.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        figures.CustomMinimumSize = new Vector2(FiguresWidth, 0);
        var headline = Ui.Label(best.Figure is { } figure ? Ui.Number(figure.Point) : Tr.Get("Stuck"), TextStyle.Value);
        headline.HorizontalAlignment = HorizontalAlignment.Right;
        figures.AddChild(headline);
        if (best.Deaths is { } deaths)
        {
            var perHour = Ui.Label(Tr.Format("{0} deaths / h", Ui.Number(deaths.Point)), TextStyle.Tiny);
            perHour.HorizontalAlignment = HorizontalAlignment.Right;
            figures.AddChild(perHour);
        }
        var against = Ui.Add(line, Ui.Row(4));
        against.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        against.CustomMinimumSize = new Vector2(GainWidth, 0);
        against.Alignment = BoxContainer.AlignmentMode.End;
        if (best.Gain is { } gain)
        {
            // The gain in green or red, then "vs A" with the letter gold.
            var value = Ui.Label(Words.Gain(gain.Point), TextStyle.Label);
            Tint(value, gain.Point >= 0 ? Look.Safe : Look.Deadly);
            against.AddChild(value);
            // Translators: After a master's gain on its best setup: against one setup for all its monsters, side A.
            var vs = Ui.Label("", TextStyle.Label);
            SideLine.OnLabel(vs, Tr.Format("vs {0}", SideLine.A), TextStyle.Label);
            against.AddChild(vs);
        }
        if (best.Figure == null)
            return;
        var onB = Slayer.MonsterLoadouts.IsOnB(best);
        // Translators: Button on a master's row: put this master's best blocks and loadouts on side B.
        var use = Ui.Button(
            "",
            Look.Button4,
            Ui.Id("Cs2BestMasterUse", Ids.Brief(Ids.MasterOf(best.Master))),
            () => Slayer.MonsterLoadouts.Use(best)
        );
        use.Disabled = onB;
        // One width for "Use on B" and "On B ✓", so the columns before it stay in line.
        use.CustomMinimumSize = new Vector2(UseWidth, 0);
        SideLine.OnButton(use, onB ? Tr.Format("On {0} ✓", SideLine.B) : Tr.Format("Use on {0}", SideLine.B));
        use.TooltipText = onB
            ? Tr.Get("Side B gives this master its best blocks and loadouts")
            : Tr.Get("Puts this master's best blocks and loadouts on side B; nothing else on B changes");
        use.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(use);
    }

    private static void Tint(Label label, Color colour)
    {
        var settings = (LabelSettings)label.LabelSettings.Duplicate();
        settings.FontColor = colour;
        label.LabelSettings = settings;
    }
}
