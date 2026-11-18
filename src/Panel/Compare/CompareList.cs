using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Results;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Compare;

/// <summary>
/// A comparison as a list: column names, then one row per target with A, B, the difference B − A as a bar around 0
/// with its range, the verdict and ✕ Ignore; then the note on chance differences. Rows are reused across updates and
/// made a few per frame, as in the ranked list.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class CompareList
{
    private const int RowsPerFrame = 8;
    private const float NameWidth = 150;
    private const float SideWidth = 104;
    private const float DifferenceWidth = 250;

    // Wide enough for "Both died every fight", so the columns before it don't move when a row says it.
    private const float VerdictWidth = 164;
    private const float RankWidth = 34;
    private const float PortraitSize = 52;
    private const int Gap = 10;

    private sealed class Row
    {
        public required PanelContainer Root { get; init; }
        public required Label Rank { get; init; }
        public required PanelContainer Portrait { get; init; }
        public required Label Name { get; init; }
        public required Label Area { get; init; }
        public required Label A { get; init; }
        public required Label ARange { get; init; }
        public required PanelContainer AWarning { get; init; }
        public required Label B { get; init; }
        public required Label BRange { get; init; }
        public required PanelContainer BWarning { get; init; }
        public required Label Difference { get; init; }
        public required Label Percent { get; init; }
        public required Control Bar { get; init; }
        public required PanelContainer Verdict { get; init; }
        public required PanelContainer RanOver { get; init; }
        public required Button Ignore { get; init; }
        public string? Key { get; set; }
    }

    private readonly ScrollContainer _sideways;
    private readonly VBoxContainer _root;
    private readonly Label _headA;
    private readonly Label _headB;
    private readonly Label _headDifference;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _list;
    private readonly Label _note;
    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private readonly Stack<Row> _spare = new();
    private CompareTable? _table;
    private bool _pending;

    public CompareList()
    {
        // The columns keep their widths, so a room narrower than all of them scrolls the rows and their headings
        // sideways together, here rather than the whole window.
        _sideways = new ScrollContainer
        {
            Name = "Cs2CompareSideways",
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _root = Ui.Add(_sideways, Ui.Column(4, "Cs2CompareList"));
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // The headings sit inside the rows' own margins and take the rows' column widths, so each lines up over its
        // column.
        var rowBox = Look.Box(Art.RowBox);
        var headBox = new MarginContainer();
        headBox.AddThemeConstantOverride("margin_left", (int)rowBox.ContentMarginLeft);
        headBox.AddThemeConstantOverride("margin_right", (int)rowBox.ContentMarginRight);
        _root.AddChild(headBox);
        var head = Ui.Add(headBox, Ui.Row(Gap, "Cs2CompareColumns"));
        head.AddChild(Head(Tr.Get("Target"), RankWidth + Gap + PortraitSize + Gap + NameWidth));
        // A, B and the difference sort the rows when clicked; the sorted one is marked.
        _headA = Sorts(Head("", SideWidth), CompareOrder.A, "Cs2CompareSortA");
        head.AddChild(_headA);
        _headB = Sorts(Head("", SideWidth), CompareOrder.B, "Cs2CompareSortB");
        head.AddChild(_headB);
        _headDifference = Sorts(Head("", DifferenceWidth), CompareOrder.Difference, "Cs2CompareSortDifference");
        head.AddChild(_headDifference);
        var verdictHead = Head(Tr.Get("Verdict"), VerdictWidth);
        verdictHead.HorizontalAlignment = HorizontalAlignment.Center;
        head.AddChild(verdictHead);
        _scroll = new ScrollContainer
        {
            Name = "Cs2CompareScroll",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _root.AddChild(ScrollFade.Wrap(_scroll));
        ScrollFoot.Hold(_scroll);
        _list = Ui.Add(_scroll, Ui.Column(6, "Cs2CompareRows"));
        _list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _note = Ui.Label(
            Tr.Get("About 1 in 20 targets can show a difference by chance."),
            TextStyle.Small,
            "Cs2CompareNote",
            wrap: true
        );
    }

    public Control Root => _sideways;

    public int Count => _rows.Count;

    public bool Pending => _pending;

    public bool NoteShown => _note.Visible;

    /// <summary>The note under the rows, which the results view places with what sits under them.</summary>
    public Control Note => _note;

    private static Label Head(string text, float width)
    {
        var label = Ui.Clipped(text, TextStyle.Tiny);
        // Exactly its column's width: a heading that stretched would drift off the column under it.
        label.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        label.CustomMinimumSize = new Vector2(width, 0);
        return label;
    }

    private static Label Sorts(Label head, CompareOrder order, string name)
    {
        head.Name = name;
        head.MouseFilter = Control.MouseFilterEnum.Stop;
        head.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        head.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                PanelState.SetCompareOrder(order);
        };
        return head;
    }

    // A header's text, with ▼ and the highlight when the rows are sorted by it.
    private static void SetHead(Label head, string text, CompareOrder order)
    {
        var sorted = PanelState.CompareOrder == order;
        SideLine.OnLabel(head, sorted ? text + "  ▼" : text, TextStyle.Tiny, sorted ? Look.Best : null);
        head.TooltipText = text + "\n" + Marks.Muted(Tr.Get("Click to sort by this column."));
    }

    /// <summary>Shows <paramref name="table"/>; makes at most a few new rows per call.</summary>
    public void Show(ComparedRuns comparison, CompareTable table)
    {
        _table = table;
        var saved = comparison.Record.Kind == ComparisonKind.NowAgainstSaved;
        SetHead(_headA, CompareText.Side(SideLine.A, comparison.A, saved), CompareOrder.A);
        SetHead(_headB, CompareText.Side(SideLine.B, comparison.B, saved: false), CompareOrder.B);
        SetHead(
            _headDifference,
            Tr.Format("Difference {0} − {1} · 95% range", SideLine.B, SideLine.A),
            CompareOrder.Difference
        );
        _note.Visible = comparison.Record.Targets.Count > 1;
        var scale = DiffBar.ScaleOf(table.Rows.Select(r => r.Difference.Delta));
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
        foreach (var tableRow in table.Rows)
        {
            if (!_rows.TryGetValue(tableRow.Key, out var row))
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
                row.Key = tableRow.Key;
                row.Root.Name = Ui.Id("Cs2CompareRow", tableRow.Key);
                row.Ignore.Name = Ui.Id("Cs2RowIgnore", tableRow.Key);
                _rows[tableRow.Key] = row;
            }
            Fill(row, tableRow, scale);
            Warn(row.AWarning, comparison.A.Results.GetValueOrDefault(tableRow.Key));
            Warn(row.BWarning, comparison.B.Results.GetValueOrDefault(tableRow.Key));
            // The chip names the side whose trials ran over; its hover says how many, and why.
            var (letter, side) =
                tableRow.Difference.RanOverB != null ? ("B", comparison.B)
                : tableRow.Difference.RanOverA != null ? ("A", comparison.A)
                : ("", null as Run);
            row.RanOver.TooltipText = side?.Results.GetValueOrDefault(tableRow.Key) is { } overRun
                ? letter + ": " + RanOverChip.Hover(overRun)
                : "";
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
    }

    /// <summary>Redraws the selection outline without re-ranking.</summary>
    public void RefreshMarks()
    {
        foreach (var row in _rows.Values)
            if (row.Key != null)
                Mark(row, row.Key);
    }

    private static Row Make()
    {
        var root = Look.Panel(Art.RowBox);
        root.CustomMinimumSize = new Vector2(0, 64);
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        var line = Ui.Add(root, Ui.Row(Gap));
        var rank = Ui.Label("", TextStyle.Heading);
        rank.HorizontalAlignment = HorizontalAlignment.Center;
        rank.CustomMinimumSize = new Vector2(RankWidth, 0);
        line.AddChild(rank);
        var portrait = Slot.Make(null, PortraitSize);
        line.AddChild(portrait);
        var names = Ui.Add(line, Ui.Column(0));
        names.CustomMinimumSize = new Vector2(NameWidth, 0);
        names.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var name = Ui.Clipped("", TextStyle.BodyStrong);
        names.AddChild(name);
        var area = Ui.Clipped("", TextStyle.Small);
        names.AddChild(area);
        // A side's value over its range, and its warning mark centred on both.
        (Label, Label, PanelContainer) Side(string side)
        {
            var cell = Ui.Add(line, Ui.Row(4));
            cell.CustomMinimumSize = new Vector2(SideWidth, 0);
            var column = Ui.Add(cell, Ui.Column(0));
            column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            column.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            var value = Ui.Clipped("", TextStyle.BodyStrong);
            column.AddChild(value);
            var range = Ui.Clipped("", TextStyle.Tiny);
            column.AddChild(range);
            var warning = RiskMark.Make("Cs2RowWarning" + side);
            warning.Visible = false;
            warning.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            cell.AddChild(warning);
            return (value, range, warning);
        }
        var (a, aRange, aWarning) = Side("A");
        var (b, bRange, bWarning) = Side("B");
        var difference = Ui.Add(line, Ui.Column(4));
        difference.CustomMinimumSize = new Vector2(DifferenceWidth, 0);
        difference.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var top = Ui.Add(difference, Ui.Row(8));
        var value = Ui.Label("", TextStyle.BodyStrong);
        top.AddChild(value);
        var percent = Ui.Clipped("", TextStyle.Tiny);
        percent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(percent);
        var bar = DiffBar.Make("", 12);
        difference.AddChild(bar);
        // The badge centred in a column as wide as its heading's.
        var verdictCell = Ui.Add(line, new CenterContainer { CustomMinimumSize = new Vector2(VerdictWidth, 0) });
        var badges = Ui.Add(verdictCell, Ui.Column(4));
        var verdict = CompareText.Badge();
        badges.AddChild(verdict);
        var ranOver = RanOverChip.Make("Cs2CmpRanOver");
        badges.AddChild(ranOver);
        line.AddChild(Ui.Spacer());
        Row? made = null;
        var ignore = IgnoreButton.Make(() => made?.Key);
        line.AddChild(ignore);
        // Room after the last button, which otherwise meets the row's textured edge.
        line.AddChild(Ui.Spacer(false, 8));
        var row = new Row
        {
            Root = root,
            Rank = rank,
            Portrait = portrait,
            Name = name,
            Area = area,
            A = a,
            ARange = aRange,
            AWarning = aWarning,
            B = b,
            BRange = bRange,
            BWarning = bWarning,
            Difference = value,
            Percent = percent,
            Bar = bar,
            Verdict = verdict,
            RanOver = ranOver,
            Ignore = ignore,
        };
        made = row;
        root.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && row.Key != null)
                PanelState.Select(row.Key);
            else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                IgnoreButton.RunOnly(row.Key);
        };
        return row;
    }

    // A side's warning mark: shown when its highest hit reaches its lowest HP, and in red when the side died
    // every fight.
    private static void Warn(PanelContainer mark, TargetResult? result) =>
        RiskMark.Set(mark, result == null ? null : Risk.Of(result), result?.NoneSurvived == true);

    private static void Fill(Row row, CompareTableRow tableRow, double scale)
    {
        var d = tableRow.Difference;
        var game = LiveGame.Find();
        var info = PanelState.Catalog?.Find(d.Key);
        row.Root.Visible = true;
        row.Ignore.Disabled = Engine.RunService.Instance.IsActive;
        row.Rank.Text = tableRow.Rank?.ToString(Tr.Culture) ?? "–";
        Slot.Set(row.Portrait, game != null ? Art.Target(game, TargetSpec.FromKey(d.Key)) : Art.Missing);
        var (name, area) = RankList.Names(new RankRow(d.Key, d.Name, null, null, 0, d.StateB, null), info);
        Ui.SetClipped(row.Name, name);
        Ui.SetClipped(row.Area, area);
        Ui.SetClipped(row.A, CompareText.Value(d.A, d.StateA));
        Ui.SetClipped(row.ARange, d.A != null ? CompareText.Range(d.A) : Short(RankList.Reason(d.ReasonsA)));
        Ui.SetClipped(row.B, CompareText.Value(d.B, d.StateB));
        Ui.SetClipped(row.BRange, d.B != null ? CompareText.Range(d.B) : Short(RankList.Reason(d.ReasonsB)));
        var colour = DiffBar.Colour(d.Verdict, row.Difference);
        if (d.Delta != null)
        {
            row.Difference.Text = CompareText.Signed(d.Delta.Point);
            row.Difference.LabelSettings = CompareText.Coloured(colour, TextStyle.BodyStrong);
            Ui.SetClipped(row.Percent, d.SkillsDiffer ? Tr.Get("different skills") : CompareText.Percent(d.Percent));
        }
        else
        {
            row.Difference.Text = "–";
            row.Difference.LabelSettings = Look.Style(TextStyle.BodyStrong);
            Ui.SetClipped(row.Percent, Missing(d));
        }
        DiffBar.Set(row.Bar, d.Delta, scale, d.SkillsDiffer ? Verdict.None : d.Verdict);
        CompareText.SetBadge(row.Verdict, d);
        row.Verdict.Name = Ui.Id(
            d.StallA != null || d.StallB != null ? "Cs2CmpReason"
                : d.Reason != null ? "Cs2CmpDied"
                : "Cs2CmpVerdict",
            d.Key
        );
        var (side, ranOver) =
            d.RanOverB is { } b ? ("B", b)
            : d.RanOverA is { } a ? ("A", a)
            : (null, null as RanOverCount);
        RanOverChip.Set(row.RanOver, ranOver is { Trials: > 0 } r ? (double)r.RanOver / r.Trials : null, null, side);
        var waiting = d.StateA is TargetState.Queued || d.StateB is TargetState.Queued;
        row.Root.Modulate = Colors.White with { A = waiting ? 0.6f : 1f };
        row.Root.TooltipText = string.Join(
            "\n",
            new[] { Side(SideLine.A, d.ReasonsA), Side(SideLine.B, d.ReasonsB) }.Where(t => t.Length > 0)
        );
        IgnoreButton.Show(row.Ignore, d.Key, row.Portrait);
        Mark(row, d.Key);
    }

    // Why there's no difference: a side still running or waiting, or a side that couldn't fight.
    private static string Missing(Difference d)
    {
        if (
            d.StateA is TargetState.Building or TargetState.Fighting
            || d.StateB is TargetState.Building or TargetState.Fighting
        )
            return Tr.Get("waiting for both sides");
        if (d.StateA == TargetState.Queued || d.StateB == TargetState.Queued)
            return Tr.Get("waiting for both sides");
        if (d.ReasonsA != null)
            return Tr.Format("A: {0}", Short(RankList.Reason(d.ReasonsA)));
        if (d.ReasonsB != null)
            return Tr.Format("B: {0}", Short(RankList.Reason(d.ReasonsB)));
        return Tr.Get("no kills on one side");
    }

    private static string Side(string letter, IReadOnlyList<Note>? reasons) =>
        RankList.Reason(reasons) is { Length: > 0 } reason ? letter + ": " + reason : "";

    private static string Short(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
            return "";
        return reason.Length <= 34 ? reason : reason[..33] + "…";
    }

    private static void Mark(Row row, string key)
    {
        if (PanelState.Selected == key)
        {
            PixelBox.Panel(row.Root, Look.RowOutline(Colors.White with { A = 0.85f }, 0.07f));
        }
        else
        {
            PixelBox.Panel(row.Root, Look.Box(Art.RowBox));
        }
    }

    /// <summary>Describes the rows in order, for <c>cs2 ui</c>.</summary>
    public IEnumerable<string> Describe() =>
        _table?.Rows.Select(r =>
            $"{r.Rank?.ToString(CultureInfo.InvariantCulture) ?? "-"} {Ids.BriefKey(r.Key)} d {(r.Difference.Delta is { } d ? Ui.Number(d.Point, english: true) : "-")} {r.Difference.Verdict.ToString().ToLowerInvariant()}{(r.Difference.Reason != null ? " died" : "")}"
        )
        ?? [];
}
