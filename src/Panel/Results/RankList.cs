using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The ranked list: one row per target in the run, in <see cref="Ranking"/> order, reused across updates. Each row
/// shows rank, portrait, name and area, value ± range, a bar of the fraction of the best (gold for the best), the
/// deaths badge, time per kill, ✕ Ignore and a pin. The best row carries a "Best" tag and a gold edge. Targets where
/// every fight died follow the ranked ones under a heading, without a rank, their value greyed and a red mark in
/// the badge's place. Targets whose trial ran over come after them, under one heading per reason, their reason in
/// the value's place; a ranked target with ran-over trials carries a chip under its deaths badge. Rows are made a
/// few per frame, so a big run never stalls a frame.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class RankList
{
    private const int RowsPerFrame = 8;

    private sealed class Row
    {
        public required PanelContainer Root { get; init; }
        public required Label Rank { get; init; }
        public required PanelContainer Portrait { get; init; }
        public required Label Name { get; init; }
        public required PanelContainer Warning { get; init; }
        public required PanelContainer Best { get; init; }
        public required PanelContainer Died { get; init; }
        public required Label Area { get; init; }
        public required Label Value { get; init; }
        public required Label Range { get; init; }
        public required Control Bar { get; init; }
        public required PanelContainer Deaths { get; init; }
        public required PanelContainer RanOver { get; init; }
        public required Label KillTime { get; init; }
        public required Label PerKill { get; init; }
        public required Button Ignore { get; init; }
        public required Button Pin { get; init; }
        public string? Key { get; set; }
    }

    private readonly ScrollContainer _scroll;
    private readonly Control _root;
    private readonly VBoxContainer _list;
    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private readonly Stack<Row> _spare = new();
    private readonly HBoxContainer _diedHeading;
    private readonly Label _diedCount;
    private readonly Label _noneSurvived;
    private readonly ReasonGroups _groups;
    private RankTable? _table;
    private Run? _run;
    private bool _pending;

    public RankList()
    {
        _scroll = new ScrollContainer
        {
            Name = "Cs2ListScroll",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _list = Ui.Add(_scroll, Ui.Column(6, "Cs2List"));
        _root = ScrollFade.Wrap(_scroll);
        ScrollFoot.Hold(_scroll);
        _list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _diedHeading = Ui.Add(_list, Ui.Row(10, "Cs2DiedHeading"));
        _diedCount = Ui.Add(_diedHeading, Ui.Label("", TextStyle.Label));
        var rule = Ui.Add(_diedHeading, new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        rule.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _diedHeading.Visible = false;
        _noneSurvived = Ui.Add(_list, Ui.Label(Tr.Get("No target survived"), TextStyle.BodyStrong, "Cs2NoneSurvived"));
        _noneSurvived.Visible = false;
        _groups = new ReasonGroups(_list);
    }

    public Control Root => _root;

    /// <summary>Raised when a pin is refused (the pins are full), with the reason for the view to show.</summary>
    public event Action<string>? Message;

    public int Count => _rows.Count;

    /// <summary>Whether rows are still to be made; call <see cref="Show"/> again next frame.</summary>
    public bool Pending => _pending;

    /// <summary>Shows a ranking of <paramref name="run"/>; makes at most a few new rows per call.</summary>
    public void Show(Run run, RankTable table)
    {
        _run = run;
        _table = table;
        var keys = table.Rows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _rows.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            var gone = _rows[key];
            _rows.Remove(key);
            gone.Root.Visible = false;
            gone.Key = null;
            _spare.Push(gone);
        }
        var made = 0;
        _pending = false;
        var index = 0;
        var died = table.DiedCount;
        // In the best row's place when there is none: every target with a result killed the player every time.
        _noneSurvived.Visible = died > 0 && table.BestKey == null;
        _list.MoveChild(_noneSurvived, index++);
        _diedHeading.Visible = died > 0;
        _diedCount.Text = Tr.Format("Died every fight ({0})", died);
        var headed = false;
        _groups.Count(table);
        StallReason? group = null;
        foreach (var rankRow in table.Rows)
        {
            if (rankRow.Died && !headed)
            {
                headed = true;
                _list.MoveChild(_diedHeading, index++);
            }
            if (_groups.Before(rankRow, group) is { } heading)
                _list.MoveChild(heading, index++);
            group = rankRow.Stall?.Reason ?? group;
            if (!_rows.TryGetValue(rankRow.Key, out var row))
            {
                if (made >= RowsPerFrame && _spare.Count == 0)
                {
                    _pending = true;
                    break;
                }
                row = _spare.Count > 0 ? _spare.Pop() : Make();
                if (row.Root.GetParent() == null)
                {
                    _list.AddChild(row.Root);
                    made++;
                }
                row.Key = rankRow.Key;
                row.Root.Name = Ui.Id("Cs2Row", rankRow.Key);
                row.Pin.Name = Ui.Id("Cs2Pin", rankRow.Key);
                row.Died.Name = Ui.Id("Cs2RowDied", rankRow.Key);
                row.RanOver.Name = Ui.Id("Cs2RowRanOver", rankRow.Key);
                row.Ignore.Name = Ui.Id("Cs2RowIgnore", rankRow.Key);
                _rows[rankRow.Key] = row;
            }
            Fill(row, rankRow, rankRow.Key == table.BestKey);
            _list.MoveChild(row.Root, index++);
        }
    }

    public void Clear()
    {
        foreach (var row in _rows.Values)
        {
            row.Root.Visible = false;
            row.Key = null;
            _spare.Push(row);
        }
        _rows.Clear();
        _table = null;
        _run = null;
        _diedHeading.Visible = false;
        _noneSurvived.Visible = false;
        _groups.Hide();
    }

    /// <summary>Redraws the selection outline and pins without re-ranking.</summary>
    public void RefreshMarks()
    {
        if (_table == null)
            return;
        foreach (var rankRow in _table.Rows)
            if (_rows.TryGetValue(rankRow.Key, out var row))
                Mark(row, rankRow.Key, rankRow.Key == _table.BestKey);
    }

    private Row Make()
    {
        var root = Look.Panel(Art.RowBox);
        root.CustomMinimumSize = new Vector2(0, 72);
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        var line = Ui.Add(root, Ui.Row(12));
        var rank = Ui.Label("", TextStyle.Heading);
        rank.HorizontalAlignment = HorizontalAlignment.Center;
        rank.CustomMinimumSize = new Vector2(34, 0);
        line.AddChild(rank);
        var portrait = Slot.Make(null, 58);
        line.AddChild(portrait);
        var names = Ui.Add(line, Ui.Column(0));
        names.CustomMinimumSize = new Vector2(210, 0);
        names.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var nameRow = Ui.Add(names, Ui.Row(6));
        var name = Ui.Clipped("", TextStyle.BodyStrong);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameRow.AddChild(name);
        var best = BestTag.Make("Cs2RowBest");
        best.Visible = false;
        nameRow.AddChild(best);
        var warning = RiskMark.Make("Cs2RowWarning");
        warning.Visible = false;
        nameRow.AddChild(warning);
        var area = Ui.Clipped("", TextStyle.Small);
        names.AddChild(area);

        var middle = Ui.Add(line, Ui.Column(6));
        middle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        middle.SizeFlagsStretchRatio = 2;
        middle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var values = Ui.Add(middle, Ui.Row(8));
        var value = Ui.Label("", TextStyle.Value);
        values.AddChild(value);
        var range = Ui.Label("", TextStyle.Tiny);
        values.AddChild(range);
        var bar = Bar.Make("", 7);
        middle.AddChild(bar);

        var badges = Ui.Add(line, Ui.Column(4));
        badges.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // As wide in every row, with or without a ran-over chip, so the bars beside it end in one line.
        badges.CustomMinimumSize = new Vector2(RanOverChip.Widest(), 0);
        var deaths = Badge.Make();
        deaths.CustomMinimumSize = new Vector2(96, 0);
        badges.AddChild(deaths);
        var ranOver = RanOverChip.Make("Cs2RowRanOver");
        badges.AddChild(ranOver);
        var died = DiedMark.Make("Cs2RowDied");
        died.Visible = false;
        line.AddChild(died);
        var time = Ui.Add(line, Ui.Column(0));
        time.CustomMinimumSize = new Vector2(64, 0);
        time.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var killTime = Ui.Label("", TextStyle.BodyStrong);
        time.AddChild(killTime);
        var perKill = Ui.Label(Tr.Get("per kill"), TextStyle.Tiny);
        time.AddChild(perKill);
        Row? made = null;
        var ignore = IgnoreButton.Make(() => made?.Key);
        line.AddChild(ignore);
        var pin = Ui.Button("", "", "Cs2Pin");
        pin.Flat = true;
        pin.Icon = Art.File(Art.PinPath);
        pin.ExpandIcon = true;
        pin.CustomMinimumSize = new Vector2(30, 30);
        pin.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        pin.TooltipText = Tr.Format("Pin to compare (up to {0})", Preferences.MaxPins);
        line.AddChild(pin);

        var row = new Row
        {
            Root = root,
            Rank = rank,
            Portrait = portrait,
            Name = name,
            Warning = warning,
            Best = best,
            Died = died,
            Area = area,
            Value = value,
            Range = range,
            Bar = bar,
            Deaths = deaths,
            RanOver = ranOver,
            KillTime = killTime,
            PerKill = perKill,
            Ignore = ignore,
            Pin = pin,
        };
        made = row;
        root.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click && row.Key != null)
            {
                if (click.ShiftPressed)
                    TogglePin(row.Key);
                else
                    PanelState.Select(row.Key);
            }
            else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                IgnoreButton.RunOnly(row.Key);
        };
        pin.Pressed += () =>
        {
            if (row.Key != null)
                TogglePin(row.Key);
        };
        return row;
    }

    private void TogglePin(string key)
    {
        try
        {
            PanelPins.TogglePin(key);
        }
        catch (CommandException)
        {
            // The only refusal: the pins are full.
            Message?.Invoke(Tr.Note(PanelPins.PinsFull));
        }
    }

    private void Fill(Row row, RankRow rank, bool best)
    {
        var run = _run!;
        var result = run.Results.GetValueOrDefault(rank.Key);
        var game = LiveGame.Find();
        var info = PanelState.Catalog?.Find(rank.Key);
        var spec = TargetSpec.FromKey(rank.Key);
        row.Root.Visible = true;
        row.Rank.Text = rank.Died ? "" : rank.Rank?.ToString(Tr.Culture) ?? "–";
        row.Best.Visible = best;
        row.Died.Visible = rank.Died;
        row.Deaths.Visible = !rank.Died;
        Slot.Set(row.Portrait, game != null ? Art.Target(game, spec) : Art.Missing);
        var (name, area) = Names(rank, info);
        Ui.SetClipped(row.Name, name);
        RiskMark.Set(row.Warning, result == null ? null : Risk.Of(result), rank.Died);
        Ui.SetClipped(row.Area, result?.ReusedFrom is { } origin ? area + " · " + FromEarlier(origin) : area);
        var prices = PanelState.Prices();
        RanOverChip.Set(row.RanOver, rank.RanOver, result);
        if (rank.Estimate is { } estimate)
        {
            ReasonGroups.ClearReason(row.Value, row.Bar);
            Motion.CountTo(row.Value, estimate.Point, v => Ui.Number(v));
            row.Value.LabelSettings = Look.Style(TextStyle.Value);
            // A value earned while dying is shown, greyed: it is real, and it is not a result to fight for.
            row.Value.Modulate = row.Range.Modulate = Colors.White with { A = rank.Died ? 0.5f : 1f };
            var half = (estimate.High - estimate.Low) / 2;
            row.Range.Text = double.IsFinite(half) && half > 0 ? Tr.Format("± {0}", Ui.Number(half)) : "";
            Bar.Set(row.Bar, rank.OfBest, best ? Look.Best : Look.BarFill);
            Badge.Set(row.Deaths, RunCache.DeathRate(run, rank.Key));
            var killTime =
                result != null && !rank.Died ? Metrics.Estimate(result, Metrics.KillTime, TimeUnit.Hour, prices) : null;
            row.KillTime.Text = killTime is { } k ? Words.Seconds(Ui.Number(k.Point)) : "–";
            row.PerKill.Text = result?.Kind == TargetKind.Dungeon ? Tr.Get("per clear") : Tr.Get("per kill");
            row.Root.Modulate = Colors.White;
            row.Root.TooltipText = "";
            Slayer.MasterRowLine.Fill(row.Area, row.Deaths, run, result);
        }
        else if (rank.Stall != null)
        {
            Motion.ResetCount(row.Value);
            row.Value.Modulate = row.Range.Modulate = Colors.White;
            ReasonGroups.FillReason(row.Value, row.Bar, rank);
            row.Range.Text = "";
            row.Rank.Text = "";
            Badge.Set(row.Deaths, null);
            row.Deaths.Visible = false;
            row.KillTime.Text = "";
            row.PerKill.Text = "";
            row.Root.Modulate = Colors.White;
            row.Root.TooltipText = "";
        }
        else
        {
            ReasonGroups.ClearReason(row.Value, row.Bar);
            Motion.ResetCount(row.Value);
            row.Value.Modulate = row.Range.Modulate = Colors.White;
            // Progress only, never numbers, until the target is final.
            var fighting = rank.State == TargetState.Fighting && result != null && run.Trials > 0;
            row.Value.Text =
                fighting ? Tr.Format("{0} / {1} trials", result!.TrialsDone, run.Trials)
                : rank.IsRunning ? Tr.Get("Running…")
                : rank.State == TargetState.Queued ? Tr.Get("Queued")
                : Short(Reason(rank.Reasons));
            row.Value.LabelSettings = Look.Style(rank.IsRunning ? TextStyle.Value : TextStyle.Body);
            row.Range.Text = "";
            Bar.Set(
                row.Bar,
                fighting ? (double)result!.TrialsDone / run.Trials
                    : rank.IsRunning ? 1
                    : 0,
                Look.Running,
                running: rank.IsRunning
            );
            Badge.Set(row.Deaths, null);
            row.KillTime.Text = "–";
            row.Root.Modulate = Colors.White with
            {
                A =
                    rank.State == TargetState.Queued ? 0.5f
                    : rank.IsRunning ? 1f
                    : 0.75f,
            };
            row.Root.TooltipText = Reason(rank.Reasons) ?? "";
        }
        row.Ignore.Disabled = Engine.RunService.Instance.IsActive;
        IgnoreButton.Show(row.Ignore, rank.Key, row.Portrait, row.Pin);
        Mark(row, rank.Key, best);
    }

    /// <summary>The note on a result reused from an earlier run: "from an earlier run · date".</summary>
    public static string FromEarlier(ReuseOrigin origin) =>
        Tr.Format("from an earlier run · {0}", origin.Date.ToLocalTime().ToString("g", Tr.Culture));

    private static void Mark(Row row, string key, bool best)
    {
        var selected = PanelState.Selected == key;
        var pinned = PanelPins.ShownPins.Contains(key);
        if (selected || best)
        {
            PixelBox.Panel(
                row.Root,
                Look.RowOutline(selected ? Colors.White with { A = 0.85f } : Look.Best, selected ? 0.07f : 0.03f)
            );
        }
        else
        {
            PixelBox.Panel(row.Root, Look.Box(Art.RowBox));
        }
        row.Pin.Modulate = pinned ? Look.Best : Colors.White with { A = 0.45f };
        row.Pin.TooltipText = pinned ? Tr.Get("Unpin") : Tr.Format("Pin to compare (up to {0})", Preferences.MaxPins);
    }

    /// <summary>Why a target has no estimate, in the game's language (or English for replies); null with one.</summary>
    public static string? Reason(IReadOnlyList<Note>? reasons, bool english = false) =>
        reasons == null ? null
        : english ? reasons.English("; ")
        : Tr.Notes(reasons, "; ");

    private static string Short(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
            return "–";
        return reason.Length <= 40 ? reason : reason[..39] + "…";
    }

    /// <summary>A target's name and its area's name, from the catalog when it has them.</summary>
    public static (string Name, string Area) Names(RankRow rank, TargetInfo? info, bool english = false)
    {
        if (Slayer.MasterRowLine.Names(rank.Key, info?.Name) is { } master)
            return master;
        var dungeon = Words.Render(Note.Of("Dungeon"), english);
        var game = LiveGame.Find();
        if (info != null)
        {
            var group = info.Group;
            var slash = group.LastIndexOf(" / ", StringComparison.Ordinal);
            var area =
                info.Spec.Enemy == null ? dungeon
                : slash < 0 ? group
                : group[(slash + 3)..];
            return (info.Name, area);
        }
        var spec = TargetSpec.FromKey(rank.Key);
        if (game != null)
            return (
                LiveGame.NameOf(game, spec.Enemy ?? spec.Area),
                spec.Enemy == null ? dungeon : LiveGame.NameOf(game, spec.Area)
            );
        var name = Spoilers.Current.Name(rank.Name);
        var open = name.IndexOf(" (", StringComparison.Ordinal);
        return open > 0 && name.EndsWith(')') ? (name[..open], name[(open + 2)..^1]) : (name, "");
    }

    /// <summary>Describes the rows in order, for <c>cs2 ui</c>.</summary>
    public IEnumerable<string> Describe() =>
        _table?.Rows.Select(r =>
            $"{r.Rank?.ToString(CultureInfo.InvariantCulture) ?? "-"} {Ids.BriefKey(r.Key)} {(r.Estimate is { } e ? Ui.Number(e.Point, english: true) : r.Reason)} of-best {r.OfBest.ToString("0.###", CultureInfo.InvariantCulture)}{(r.Died ? " died" : "")}"
        )
        ?? [];

    /// <summary>The reason headings shown and the ran-over chips, for <c>cs2 ui</c>.</summary>
    public IEnumerable<string> DescribeMarks() =>
        ReasonGroups
            .Describe(_table)
            .Concat(
                _table
                    ?.Rows.Where(r => r.RanOver != null)
                    .Select(r =>
                        $"ran-over {Ids.BriefKey(r.Key)} {r.RanOver!.Value.ToString("0.###", CultureInfo.InvariantCulture)} {Look.RanOverBand(r.RanOver.Value)}"
                    )
                    ?? []
            );
}
