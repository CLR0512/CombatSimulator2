using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The selected target's details: a header with its rank by the chosen metric, each metric against the run's other
/// targets (bar = fraction of the best, gold tick = median) with its loot per unit of time under them, how the fights
/// went, the gear search, and the run's assumptions.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class Details
{
    public const float Width = 360;
    private const int LootShown = 12;

    private readonly PanelContainer _root;
    private readonly VBoxContainer _empty;
    private readonly VBoxContainer _body;
    private readonly PanelContainer _portrait;
    private readonly Label _name;
    private readonly Label _sub;
    private readonly Label _rank;
    private readonly Label _against;
    private readonly VBoxContainer _legend;
    private readonly Label _others;
    private readonly VBoxContainer _metrics;
    private readonly Label _lootTitle;
    private readonly HFlowContainer _loot;
    private readonly VBoxContainer _depth;
    private readonly DetailsTabs _tabs;
    private readonly Label _assumptions;
    private readonly Label _unsupported;

    private readonly Search.GearTab _gear;
    private readonly HoverPanel _hover;
    private readonly Control _owner;
    private readonly ScrollContainer _scroll;
    private TargetResult? _result;

    /// <summary>The panel; <paramref name="pickItem"/> serves its Gear tab.</summary>
    public Details(Control hoverOwner, Action<Control, Action<string>> pickItem)
    {
        _owner = hoverOwner;
        _hover = new HoverPanel(hoverOwner);
        _root = Look.Panel(Art.SideBox, "Cs2Details");
        _root.CustomMinimumSize = new Vector2(Width, 0);
        // The panel fills the Simulate tab's right column: its width is the column's, which the Setup tab follows.
        _root.Resized += () => RightColumn.Report(_root.Size.X);
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var holder = Ui.Add(_root, new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        _empty = Ui.Column(10, "Cs2DetailEmpty");
        _empty.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _empty.Alignment = BoxContainer.AlignmentMode.Center;
        var info = Ui.Picture(Art.File(Art.InfoPath), 40);
        info.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _empty.AddChild(info);
        var invite = Ui.Label(
            Tr.Get("Select a result to see how it compares with the others."),
            TextStyle.Body,
            wrap: true
        );
        invite.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.AddChild(invite);
        holder.AddChild(_empty);

        // The header and tabs stay put; only the tab's page scrolls, inside the panel it opens on.
        var gutter = Ui.Add(holder, new MarginContainer { Name = "Cs2DetailGutter" });
        gutter.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _body = Ui.Add(gutter, Ui.Column(10, "Cs2DetailBody"));
        _body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var head = Ui.Add(_body, Ui.Row(12, "Cs2DetailHead"));
        _portrait = Slot.Make(null, 96);
        head.AddChild(_portrait);
        var names = Ui.Add(head, Ui.Column(2));
        names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        names.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var nameRow = Ui.Add(names, Ui.Row(8));
        _name = Ui.Label("", TextStyle.Title, wrap: true);
        _name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameRow.AddChild(_name);
        _sub = Ui.Label("", TextStyle.Small, wrap: true);
        names.AddChild(_sub);
        _rank = Ui.Label("", TextStyle.Gold, wrap: true);
        names.AddChild(_rank);

        _tabs = new DetailsTabs(_body, gear: true);
        _scroll = _tabs.Scroll;
        var rates = _tabs.Page(DetailsTabs.Rates);
        _against = Ui.Label("", TextStyle.Tiny, wrap: true);
        rates.AddChild(_against);
        (_legend, _others) = DetailsLegend.Make(Hover);
        rates.AddChild(_legend);
        _metrics = Ui.Add(rates, Ui.Column(12));

        // The drops under the rates: loot per unit of time, then rare drops, closed until opened.
        _lootTitle = Ui.Label(Loot.Title(TimeUnit.Hour), TextStyle.Heading);
        rates.AddChild(_lootTitle);
        _loot = new HFlowContainer { Name = "Cs2Loot" };
        _loot.AddThemeConstantOverride("h_separation", 8);
        _loot.AddThemeConstantOverride("v_separation", 8);
        rates.AddChild(_loot);
        _depth = Ui.Add(rates, Ui.Column(10, "Cs2DetailDepth"));
        _gear = new Search.GearTab(_tabs.Page(DetailsTabs.Gear), _tabs.Foot, hoverOwner, pickItem);

        // One line at the panel's foot, the list itself in its hover; what CS2 couldn't simulate shows only when there is
        // any, in the warning colour.
        var foot = Ui.Add(_tabs.Foot, Ui.Row(0));
        _assumptions = Ui.Add(foot, Ui.Label("", TextStyle.Small, "Cs2Assumptions"));
        _assumptions.MouseFilter = Control.MouseFilterEnum.Stop;
        _unsupported = Ui.Add(foot, Ui.Label("", TextStyle.Small, "Cs2Unsupported"));
        _unsupported.MouseFilter = Control.MouseFilterEnum.Stop;
        _unsupported.LabelSettings = Compare.CompareText.Coloured(Look.Risky, TextStyle.Small);
        ShowNothing();
    }

    public Control Root => _root;

    public string? Key { get; private set; }

    // What is on screen, so a redraw with nothing new keeps it (and its animations) as it is.
    private string? _shownFor;

    // The sections still to fill for the shown target, one a frame: a target with many metrics and drops would
    // otherwise make all its rows on the frame it was picked.
    private readonly Queue<Action> _sections = new();

    /// <summary>Whether sections of the shown target are still to be filled.</summary>
    public bool Pending => _sections.Count > 0;

    /// <summary>Fills the next section; each keeps the last target's rows until its turn, so nothing blinks.</summary>
    public void BuildNext()
    {
        if (_sections.Count > 0)
            _sections.Dequeue()();
    }

    public void ShowNothing()
    {
        Key = null;
        _result = null;
        _shownFor = null;
        _sections.Clear();
        _empty.Visible = true;
        _body.GetParent<Control>().Visible = false;
    }

    /// <summary>Shows <paramref name="key"/> in <paramref name="run"/>, ranked by <paramref name="metric"/>.</summary>
    public void Show(Run run, string key, string metric, TimeUnit unit)
    {
        if (!run.Results.TryGetValue(key, out var result))
        {
            ShowNothing();
            return;
        }
        // The Gear tab follows the search and side A, which change without the run.
        _gear.Show(key);
        var shownFor = $"{RunCache.Stamp(run)}|{key}|{metric}|{unit}|{PanelState.Look}|{TargetTask.Follows(result)}";
        if (shownFor == _shownFor)
            return;
        // Only a panel coming from empty fades in: fading on every switch of target blinks the whole panel.
        if (_empty.Visible)
            Motion.FadeIn(_body);
        _shownFor = shownFor;
        Key = key;
        _result = result;
        _empty.Visible = false;
        _body.GetParent<Control>().Visible = true;
        var game = LiveGame.Find();
        var prices = PanelState.Prices();
        var names = PanelState.Names();
        var spec = TargetSpec.FromKey(key);
        Slot.Set(_portrait, game != null ? Art.Target(game, spec) : Art.Missing);
        var table = RunCache.Rank(run, metric, unit);
        var rankRow = table.Rows.FirstOrDefault(r => r.Key == key);
        var (name, area) = RankList.Names(
            rankRow ?? new RankRow(key, result.Name, null, null, 0, result.State, null),
            PanelState.Catalog?.Find(key)
        );
        _name.Text = name;
        _sub.Text = result.ReusedFrom is { } origin
            ? Subtitle(spec, area) + " · " + RankList.FromEarlier(origin)
            : Subtitle(spec, area);
        _sub.Text += TargetTask.Subtitle(result, names);
        var metricLabel = Words.MetricName(Metrics.Describe(table.Metric, names), unit);
        _rank.Text =
            rankRow?.Rank is { } r ? Tr.Format("#{0} of {1} by {2}", [r, table.Count, metricLabel])
            : rankRow?.Died == true ? DiedMark.Text
            : result.Stall is { } stall && !result.HasEstimates
                // Translators: Under the name of a target with no result because its trial passed the time limit; {0} is the limit in minutes.
                ? Tr.Format("Unfinished · trial limit {0} minutes", Math.Round(stall.LimitSeconds / 60, 1))
            : RankList.Reason(rankRow?.Reasons) ?? "";
        _rank.TooltipText = rankRow?.Died == true ? DiedMark.Why : "";
        // Gold is for a place among the ranked; a died target has none.
        _rank.LabelSettings = Look.Style(rankRow?.Died == true ? TextStyle.Small : TextStyle.Gold);

        _hover.Hide(); // its rows are about to be replaced
        _hoverTarget = null;
        _sections.Clear();
        // The rates change with the name above them, not a frame later; the rest can follow a frame at a time.
        FillMetrics(run, result, key, table.Count, unit);
        _sections.Enqueue(() =>
        {
            if (result.Kind == TargetKind.Master)
                _tabs.FillMaster(run, result, metric, unit);
            else
                _tabs.FillFights(result, names);
        });
        _sections.Enqueue(() => FillLoot(result, unit, prices, names));
        _sections.Enqueue(() => FillDepth(run, result, unit, names));
    }

    private void FillMetrics(Run run, TargetResult result, string key, int ranked, TimeUnit unit)
    {
        Ui.Clear(_metrics);
        var details = RunCache.Details(run, key, unit);
        _against.Text =
            details.Count > 0 ? ""
            : result.Reasons.Count > 0 ? Tr.Notes(result.Reasons, "\n")
            : result.State == TargetState.Fighting ? Tr.Format("{0} / {1} trials", result.TrialsDone, run.Trials)
            : Tr.Get("No results for this target yet.");
        _against.Visible = details.Count == 0;
        _legend.Visible = details.Count > 0;
        _others.Text = Tr.Plural("vs {0} other", "vs {0} others", ranked - 1, ranked - 1);
        // Drops and costs this target never has are noise here; the four fight metrics always show. Items and rare
        // events are in the loot below, not repeated as rows.
        foreach (
            var detail in details.Where(d =>
                (d.Estimate.Point != 0 || !d.Metric.Key.Contains(':', StringComparison.Ordinal))
                && d.Metric.Key.Split(':')[0] is not ("item" or "rare" or "fight")
            )
        )
        {
            _metrics.AddChild(MetricRow(run, detail, unit));
        }
    }

    private void FillLoot(TargetResult result, TimeUnit unit, PriceTable prices, Func<string, string> names)
    {
        var game = LiveGame.Find();
        Ui.Clear(_loot);
        var loot = Loot.All(result, unit, prices);
        _lootTitle.Text = Loot.Title(unit);
        _lootTitle.Visible = loot.Count > 0;
        if (game != null)
            foreach (var (lootKey, value) in loot.Take(LootShown))
                _loot.AddChild(
                    Slot.WithCount(
                        Art.Metric(game, lootKey),
                        Ui.Number(value),
                        48,
                        Words.Metric(Metrics.Describe(lootKey, names))
                    )
                );
    }

    private void FillDepth(Run run, TargetResult result, TimeUnit unit, Func<string, string> names)
    {
        var game = LiveGame.Find();
        Ui.Clear(_depth);
        if (RareDropsSection.Make(result, unit, names) is { } rare)
            _depth.AddChild(rare.Root);
        _depth.Visible = _depth.GetChildCount() > 0;

        var assumptions = ChangeNotes.Assumptions(
            run,
            result,
            id => game != null ? LiveGame.NameOf(game, id) : Ids.Brief(id)
        );
        var unsupported = ChangeNotes.NotSimulated(result);
        var assumed = Tr.Plural("{0} assumption", "{0} assumptions", assumptions.Count, assumptions.Count);
        _assumptions.Text = "ⓘ " + assumed;
        // Translators: After the assumptions line at the foot of a target's details, only when CS2 had to block
        // something in its fights; {0} is how many. Its hover lists them.
        _unsupported.Text =
            unsupported.Count == 0
                ? ""
                : " · " + Tr.Plural("{0} not simulated", "{0} not simulated", unsupported.Count, unsupported.Count);
        _unsupported.Visible = unsupported.Count > 0;
        _unsupported.TooltipText = string.Join("\n", unsupported.Select(a => "· " + Tr.Note(a)));
        _assumptions.TooltipText = string.Join("\n", assumptions.Select(a => "· " + Tr.Note(a)));
    }

    /// <summary>Describes the shown target's risk, warning, stages and rare drops, for <c>cs2 ui</c>.</summary>
    public void Describe(System.Text.StringBuilder text)
    {
        if (_result is not { } result)
            return;
        var risk = Risk.Of(result);
        text.Append("\nrisk=")
            .Append(
                risk is null ? "none"
                : !risk.Recorded ? "not-recorded"
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"highest {risk.HighestHit} lowest {(risk.LowestHp is { } l ? l.ToString(System.Globalization.CultureInfo.InvariantCulture) : "every-fight-died")} close-calls {risk.CloseCalls!.Point:0.###}"
                )
            );
        text.Append("\nwarning=").Append(risk is { Warning: true } ? "yes" : "no");
        text.Append("\ntab=").Append(_tabs.Shown);
        _gear.Describe(text);
        text.Append('\n').Append(FightBlock.Describe(result));
        if (_body.FindChild("Cs2RiskOver", recursive: true, owned: false) is Label { LabelSettings: { } style } over)
        {
            // Cut when the text as drawn is wider than the room the label got.
            var width = style.Font.GetStringSize(over.Text, HorizontalAlignment.Left, -1, style.FontSize).X;
            text.Append("\nrisk-heading \"")
                .Append(over.Text)
                .Append("\" clipped=")
                .Append(width > over.Size.X + 0.5f ? "yes" : "no");
        }
        if (result.Kind == TargetKind.Dungeon)
            text.Append("\nfights=")
                .Append(StageBreakdown.Of(result, result.Stages).Rows.Count)
                .Append(PanelState.Prefs.FightsOpen ? " open" : " closed");
        text.Append("\nrare=")
            .Append(RareDrops.Rows(result, PanelState.Prefs.Unit, PanelState.Prices()).Count)
            .Append(PanelState.Prefs.RareDropsOpen ? " open" : " closed");
    }

    private static string Subtitle(TargetSpec spec, string area)
    {
        var parts = new List<string> { area };
        if (LiveGame.EnemyLevel(spec.Enemy) is { } level)
            parts.Add(Tr.Format("Lv {0}", level));
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    /// <summary>The bars' median marker, as a small icon for legends and hover text.</summary>
    private static Control MedianIcon()
    {
        var tick = new Control
        {
            CustomMinimumSize = new Vector2(10, 14),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        tick.Draw += () => MedianMark.Paint(tick, 5, 6, 13);
        return tick;
    }

    // CS2's hover panel for a control: filled on the first mouse motion over it (motion arrives even where the
    // enter signal doesn't, as with the bridge), following the mouse, hidden when it leaves.
    private Control? _hoverTarget;

    private void Hover(Control target, Action<VBoxContainer> fill)
    {
        target.GuiInput += e =>
        {
            if (e is not InputEventMouseMotion motion)
                return;
            if (!ReferenceEquals(_hoverTarget, target) || !_hover.Visible)
            {
                _hoverTarget = target;
                _hover.Reset();
                fill(_hover.Content);
            }
            _hover.ShowAt(target.GetGlobalTransform() * motion.Position, _owner.GetGlobalRect());
        };
        target.MouseExited += () =>
        {
            if (!ReferenceEquals(_hoverTarget, target))
                return;
            _hoverTarget = null;
            _hover.Hide();
        };
    }

    private static string Format(string metric, double value) =>
        metric == Metrics.DeathRate ? Ui.Percent(value)
        : metric == Metrics.KillTime ? Words.Seconds(Ui.Number(value))
        : Ui.Number(value);

    // What the bar means, in numbers: this target against the best, the median and the worst.
    private static void Explain(VBoxContainer content, Run run, DetailRow detail, TimeUnit unit)
    {
        var key = detail.Metric.Key;
        var table = RunCache.Rank(run, key, unit);
        content.AddChild(
            Ui.Label(
                detail.Died
                    ? Tr.Format(
                        "{0}: {1}  ({2})",
                        [Words.MetricName(detail.Metric, unit), Format(key, detail.Estimate.Point), DiedMark.Text]
                    )
                    : Tr.Format(
                        "{0}: {1}  (#{2} of {3})",
                        [
                            Words.MetricName(detail.Metric, unit),
                            Format(key, detail.Estimate.Point),
                            detail.Rank,
                            detail.Count,
                        ]
                    ),
                TextStyle.BodyStrong
            )
        );
        // The best is the best ranked target: with every target dead in every fight there is none to name.
        if (table.Rows.FirstOrDefault(r => r.Rank != null) is { Estimate: { } best } top)
        {
            var (name, _) = RankList.Names(top, PanelState.Catalog?.Find(top.Key));
            content.AddChild(
                DetailsLegend.Line(DetailsLegend.Swatch(), Tr.Format("Best: {0}, {1}", [Format(key, best.Point), name]))
            );
        }
        if (table.Median is { } median)
            content.AddChild(DetailsLegend.Line(MedianIcon(), Tr.Format("Median: {0}", Format(key, median))));
        if (detail.LowerIsBetter && table.Worst is { } worst)
            content.AddChild(Ui.Label(Tr.Format("Worst: {0}", Format(key, worst)), TextStyle.Body));
        if (detail.Died)
        {
            content.AddChild(Ui.Label(DiedMark.Why, TextStyle.Small, wrap: true));
            return;
        }
        var how =
            !detail.LowerIsBetter
                ? Tr.Format("The bar is this value as a share of the best: {0}.", Ui.Percent(detail.OfBest))
            : table.Best is > 0
                ? Tr.Format(
                    "Lower is better: the bar is the best as a share of this value: {0}.",
                    Ui.Percent(detail.OfBest)
                )
            : Tr.Get("Lower is better and the best is zero: the bar runs from the worst (empty) to the best (full).");
        content.AddChild(Ui.Label(how, TextStyle.Small));
    }

    private VBoxContainer MetricRow(Run run, DetailRow detail, TimeUnit unit)
    {
        var column = Ui.Column(4, Ui.Id("Cs2DetailMetric", detail.Metric.Key));
        column.MouseFilter = Control.MouseFilterEnum.Pass;
        Hover(column, content => Explain(content, run, detail, unit));
        var line = Ui.Add(column, Ui.Row(6));
        var name = Ui.Clipped(Words.MetricName(detail.Metric, unit), TextStyle.Label);
        name.TooltipText = ""; // the row's hover panel, which names the metric, shows instead
        line.AddChild(name);
        var point = detail.Estimate.Point;
        var value =
            detail.Metric.Key == Metrics.DeathRate ? Ui.Percent(point)
            : detail.Metric.Key == Metrics.KillTime ? Words.Seconds(Ui.Number(point))
            : Ui.Number(point);
        line.AddChild(Ui.Label(value, TextStyle.BodyStrong));
        var safest = detail.Metric.Key is Metrics.DeathRate or Metrics.Deaths && point == 0;
        var place =
            detail.Died ? "–"
            : safest ? Tr.Get("safest")
            : Tr.Format("#{0} of {1}", [detail.Rank, detail.Count]);
        var rank = Ui.Label(place, TextStyle.Tiny);
        rank.CustomMinimumSize = new Vector2(52, 0);
        rank.HorizontalAlignment = HorizontalAlignment.Right;
        line.AddChild(rank);
        var range = RangeBox.Make(Ui.Id("Cs2DetailRange", detail.Metric.Key));
        RangeBox.Set(range, detail.Spot, detail.Box);
        column.AddChild(range);
        return column;
    }
}
