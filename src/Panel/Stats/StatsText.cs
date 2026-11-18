using System.Text;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Stats;

/// <summary>
/// The full stats view's rows as plain text: the stats in aligned columns, then the modifiers by group. Inside a code
/// block, so the columns stay aligned in a Discord message and read as they are in a bug report.
/// </summary>
internal static class StatsText
{
    /// <summary>
    /// The stats and modifiers of <paramref name="sheets"/> (one side, or A and B) as the view shows them:
    /// <paramref name="onlyDifferences"/> and <paramref name="search"/> narrow them the same way.
    /// </summary>
    public static string Of(IReadOnlyList<SetupSheet> sheets, bool onlyDifferences, string search)
    {
        var two = sheets.Count > 1;
        var only = two && onlyDifferences;
        var text = new StringBuilder("```\n");
        text.Append(
                two
                    ? Tr.Format(
                        "Combat Simulator 2 · A {0} · B {1}",
                        [Words.Setup(sheets[0].Setup), Words.Setup(sheets[1].Setup)]
                    )
                    : Tr.Format("Combat Simulator 2 · {0}", Words.Setup(sheets[0].Setup))
            )
            .Append('\n');

        var all = FullStatsView.StatsOf(sheets);
        var stats = all.Where(d => !only || d.Differs).ToList();
        var names = stats.Select(FullStatsView.Name).ToList();
        var width = names.Count > 0 ? names.Max(n => n.Length) : 0;
        text.Append('\n').Append(Tr.Get("Stats"));
        if (two)
            text.Append(" · ").Append(Tr.Format("differing {0} of {1}", [all.Count(d => d.Differs), all.Count]));
        text.Append('\n');
        if (two)
            text.Append(' ', width)
                .Append(Column("A"))
                .Append(Column("B"))
                .Append(Column(Tr.Get("B − A")))
                .Append('\n');
        for (var i = 0; i < stats.Count; i++)
        {
            var d = stats[i];
            text.Append(names[i].PadRight(width));
            if (two)
                text.Append(Column(d.A?.Text ?? "—"))
                    .Append(Column(d.B?.Text ?? "—"))
                    .Append(Column(FullStatsView.Delta(d)));
            else
                text.Append(Column((d.A ?? d.B)!.Text));
            text.Append('\n');
        }

        var modifiers = FullStatsView
            .ModifiersOf(sheets)
            .Where(s => (!only || s.OneSide) && ModifierSearch.Matches(s.Line, search))
            .ToList();
        text.Append('\n').Append(Tr.Get("Modifiers")).Append('\n');
        foreach (var group in modifiers.GroupBy(s => s.Line.Group).OrderBy(g => g.Key))
        {
            text.Append(FullStatsView.GroupName(group.Key)).Append('\n');
            foreach (var side in group)
            {
                text.Append("  ").Append(FullStatsView.LineText(side.Line));
                if (two)
                    text.Append("  [")
                        .Append(
                            side.InA && side.InB ? Tr.Get("A B")
                            : side.InA ? Tr.Get("A only")
                            : Tr.Get("B only")
                        )
                        .Append(']');
                text.Append('\n');
            }
        }
        return text.Append("```").ToString();
    }

    // A right-aligned value column, wide enough for the stats' longest values.
    private static string Column(string value) => value.PadLeft(12);
}
