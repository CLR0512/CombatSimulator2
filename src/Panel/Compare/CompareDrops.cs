using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Compare;

/// <summary>
/// A comparison's drops, under its rates. Most only follow the kill rate, so that is said once and they fold away;
/// rare items read as the time one takes; the drops that change per kill stay open, as the ones worth reading.
/// </summary>
internal sealed class CompareDrops
{
    // Whether "Drop rate unchanged" is open; kept while the panel stands, so moving between targets keeps it.
    private bool _sameOpen;

    public VBoxContainer Root { get; } = Ui.Column(0, "Cs2CompareDrops");

    /// <summary>
    /// Shows <paramref name="drops"/> (each item metric with its difference) of <paramref name="key"/>;
    /// <paramref name="kills"/> is the kill rate's difference, <paramref name="art"/> gives item icons.
    /// </summary>
    public void Show(
        Run a,
        Run b,
        string key,
        TimeUnit unit,
        List<(Metric First, Difference Second)> drops,
        Difference? kills,
        Func<string, Texture2D?>? art
    )
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(drops);
        var column = Root;
        Ui.Clear(column);
        column.Visible = drops.Count > 0;
        if (drops.Count == 0)
            return;
        var ra = a.Results.GetValueOrDefault(key);
        var rb = b.Results.GetValueOrDefault(key);
        var perKill = Differences.ForTarget(
            a,
            b,
            key,
            drops.Select(d => d.First.Key),
            TimeUnit.Kill,
            PanelState.Prices()
        );
        var grouped = drops
            .Zip(perKill)
            .Select(p =>
                (
                    Metric: p.First.First,
                    Difference: p.First.Second,
                    PerKill: p.Second,
                    Group: DropGroups.Of(ra, rb, p.First.First.Key["item:".Length..], p.Second)
                )
            )
            .ToList();
        column.AddChild(CompareCells.Heads(Words.MetricName(Metrics.Describe(Metrics.Drops), unit)));
        if (kills is { Verdict: not Verdict.None } && Shared(kills) is { } shared)
        {
            var note = Look.Panel(Art.RowBox, "Cs2CompareDropsShared");
            column.AddChild(note);
            note.AddChild(Ui.Label(shared, TextStyle.Small, wrap: true));
        }
        var rare = grouped.Where(g => g.Group == DropGroup.Rare).ToList();
        if (rare.Count > 0)
        {
            // Translators: Comparison, heading over rare drops shown as the time one takes on each side.
            column.AddChild(GroupHead(Tr.Get("Rare · time to first drop")));
            foreach (var g in rare)
            {
                var item = g.Metric.Key["item:".Length..];
                string Time(TargetResult? r) =>
                    r is { HasEstimates: true } && ForOnes.Time(r, item) is { } t ? Words.Duration(t.Point) : "–";
                var chance = (ra ?? rb)
                    ?.Drops?.Where(d => d.Id == item && d.Source != DropSource.RareEvent && d.Chance != null)
                    .Sum(d => d.Chance!.Value);
                var rates = Tr.Format(
                    "{0} → {1} {2}",
                    [
                        CompareCells.Side(ra, g.Difference.A, g.Metric.Key),
                        CompareCells.Side(rb, g.Difference.B, g.Metric.Key),
                        Words.PerUnitShort(unit),
                    ]
                );
                var sub = chance is { } c and > 0
                    // Translators: Under a rare drop in a comparison: {0} its chance, "0.12%"; {1} its rates, A → B.
                    ? Tr.Format("{0} per kill · {1}", [Words.Chance(c), rates])
                    : rates;
                column.AddChild(DropRow(g.Metric, g.Difference, Time(ra), Time(rb), sub, art));
            }
        }
        var differing = grouped.Where(g => g.Group == DropGroup.NotWithKills).ToList();
        if (differing.Count > 0)
        {
            // Translators: Comparison, heading over drops whose chance per kill differs between A and B.
            column.AddChild(GroupHead(Tr.Get("Drop rate changed")));
            foreach (var g in differing)
            {
                var sub = g.PerKill.Percent is { } p
                    // Translators: Under a drop in a comparison: {0} how its drop rate per kill changes, "-6.5%".
                    ? Tr.Format("{0} drop rate", CompareText.SignedPercent(p.Point))
                    : null;
                column.AddChild(
                    DropRow(
                        g.Metric,
                        g.Difference,
                        CompareCells.Side(ra, g.Difference.A, g.Metric.Key),
                        CompareCells.Side(rb, g.Difference.B, g.Metric.Key),
                        sub,
                        art
                    )
                );
            }
        }
        var same = grouped.Where(g => g.Group == DropGroup.WithKills).ToList();
        if (same.Count > 0)
        {
            // Room above, as the group's heading; the rows it opens are padded on their own.
            var foldBox = Ui.Add(column, new MarginContainer());
            foldBox.AddThemeConstantOverride("margin_top", 10);
            foldBox.AddThemeConstantOverride("margin_bottom", 4);
            var fold = Ui.Add(foldBox, Ui.Row(6, "Cs2CompareDropsSame"));
            fold.MouseFilter = Control.MouseFilterEnum.Stop;
            fold.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            var chevron = Ui.Add(fold, Ui.Label(_sameOpen ? "▾" : "▸", TextStyle.Small));
            // Translators: Comparison, the folded drops whose chance per kill is the same for A and B: {0} how many.
            var label = Ui.Add(
                fold,
                Ui.Label(Tr.Format("Drop rate unchanged · {0}", Ui.Number(same.Count)), TextStyle.Small)
            );
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var icons = Ui.Add(fold, Ui.Row(2));
            if (art != null)
                foreach (var g in same.Take(6))
                    icons.AddChild(Slot.Make(art(g.Metric.Key), 20));
            var body = Ui.Add(column, Ui.Column(0, "Cs2CompareDropsSameRows"));
            foreach (var g in same)
                body.AddChild(
                    DropRow(
                        g.Metric,
                        g.Difference,
                        CompareCells.Side(ra, g.Difference.A, g.Metric.Key),
                        CompareCells.Side(rb, g.Difference.B, g.Metric.Key),
                        null,
                        art
                    )
                );
            body.Visible = _sameOpen;
            icons.Visible = !_sameOpen;
            fold.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                    return;
                _sameOpen = !_sameOpen;
                body.Visible = _sameOpen;
                icons.Visible = !_sameOpen;
                chevron.Text = _sameOpen ? "▾" : "▸";
            };
        }
    }

    // A small gold heading over a group of drops, with room above it.
    private static Label GroupHead(string text)
    {
        var head = Ui.Label(text, TextStyle.Tiny);
        head.LabelSettings = SideLine.Gold(TextStyle.Tiny);
        head.CustomMinimumSize = new Vector2(0, 26);
        head.VerticalAlignment = VerticalAlignment.Bottom;
        return head;
    }

    // The kill rate's change, said once over the drops; null when it has no percent.
    private static string? Shared(Difference kills)
    {
        if (kills.Percent is not { } percent)
            return null;
        if (kills.Verdict == Verdict.NoClearDifference)
            // Translators: Comparison, over the drops, when B's kill rate is about A's.
            return Tr.Get("B kills about as fast.");
        var share = Ui.Percent(Math.Abs(percent.Point));
        return percent.Point < 0
            // Translators: Comparison, over the drops: {0} how much fewer kills B gets, "43%".
            ? Tr.Format("B gets {0} fewer kills.", share)
            // Translators: Comparison, over the drops: {0} how much more kills B gets, "262%".
            : Tr.Format("B gets {0} more kills.", share);
    }

    // A drop: its icon and name on one line, A, B and the change in the table's columns under it, then an optional
    // line; a long name keeps the values in their columns rather than squeezing between them.
    private static VBoxContainer DropRow(
        Metric metric,
        Difference difference,
        string a,
        string b,
        string? sub,
        Func<string, Texture2D?>? art
    )
    {
        var (root, content) = CompareCells.Row(Ui.Id("Cs2CompareDrop", metric.Key));
        var top = Ui.Add(content, Ui.Row(8));
        if (art != null)
            top.AddChild(Slot.Make(art(metric.Key), 28));
        var name = Ui.Add(
            top,
            Ui.Label(
                metric.Subject is { } subject ? Tr.Note(subject) : Words.Metric(metric),
                TextStyle.Small,
                wrap: true
            )
        );
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var values = Ui.Add(content, Ui.Row(8));
        var gap = Ui.Add(values, new Control());
        gap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        values.AddChild(CompareCells.Value(a));
        values.AddChild(CompareCells.Value(b));
        CompareCells.SetDifference(Ui.Add(values, CompareCells.Change()), difference);
        if (sub != null)
        {
            var under = Ui.Add(content, new MarginContainer());
            under.AddThemeConstantOverride("margin_left", art != null ? 36 : 0);
            under.AddChild(Ui.Label(sub, TextStyle.Tiny, wrap: true));
        }
        return root;
    }
}
