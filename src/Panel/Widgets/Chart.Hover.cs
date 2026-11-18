using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Results;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

// Pointer input over the chart: clicks on bars and bands, and the hover panel.
internal sealed partial class Chart
{
    private void Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion:
                Hover(motion.Position);
                break;
            // A right-click on a portrait runs that target alone, as on the target strip.
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } only:
                if (ToggleAt(only.Position) is { } alone)
                {
                    if (!Engine.RunService.Instance.IsActive)
                        PanelState.RunOnly([alone]);
                    _canvas.AcceptEvent();
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                if (ToggleAt(click.Position) is { } key)
                {
                    if (!Engine.RunService.Instance.IsActive)
                        PanelState.SetIgnored([key], !PanelState.Ignored.Contains(key));
                    _canvas.AcceptEvent();
                    return;
                }
                foreach (var (rect, groups) in _bandHits)
                {
                    if (rect.HasPoint(click.Position))
                    {
                        if (groups[0].AllEmpty)
                            PanelState.ToggleOpen(EmptyRun(groups[0]));
                        else
                            PanelState.ToggleFold(groups[0].Id);
                        _canvas.AcceptEvent();
                        return;
                    }
                }
                if (BarAt(click.Position) is not { } bar)
                    return;
                if (click.ShiftPressed)
                {
                    try
                    {
                        PanelPins.TogglePin(bar.Key);
                    }
                    catch (CommandException)
                    {
                        // The only refusal: the pins are full.
                        Message?.Invoke(Tr.Note(PanelPins.PinsFull));
                    }
                }
                else
                {
                    PanelState.Select(bar.Key);
                }
                _canvas.AcceptEvent();
                break;
        }
    }

    // The area and its neighbours without results: a stub opens them together, and they collapse together.
    private List<string> EmptyRun(Group group)
    {
        var at = _groups.IndexOf(group);
        var first = at;
        var last = at;
        while (first > 0 && _groups[first - 1].AllEmpty)
            first--;
        while (last < _groups.Count - 1 && _groups[last + 1].AllEmpty)
            last++;
        return [.. _groups.Skip(first).Take(last - first + 1).Select(g => g.Id)];
    }

    // Between two areas' columns is a few pixels of nothing: a point there belongs to the column beside it, so the
    // hover doesn't drop out as the pointer crosses the chart.
    private static bool Near(Rect2 rect, Vector2 position) => rect.GrowIndividual(4, 0, 4, 0).HasPoint(position);

    private BarItem? BarAt(Vector2 position) =>
        _barHits.FirstOrDefault(h => h.Rect.HasPoint(position)).Bar
        ?? _barHits.FirstOrDefault(h => Near(h.Rect, position)).Bar;

    private string? ToggleAt(Vector2 position) =>
        _toggleHits.FirstOrDefault(h => h.Rect.HasPoint(position)).Key
        ?? _toggleHits.FirstOrDefault(h => Near(h.Rect, position)).Key;

    // A line of the hover: shown in the game's language, kept in English for replies in dev builds.
    private void HoverLine(Node parent, Func<bool, string> text, TextStyle style)
    {
        parent.AddChild(Ui.Label(text(false), style));
        KeepEnglish(text);
    }

    private void Hover(Vector2 position)
    {
        if (ToggleAt(position) is { } toggled)
        {
            _hover.Reset();
            ForgetEnglish();
            HoverLine(
                _hover.Content,
                en =>
                    Words.Render(
                        PanelState.Ignored.Contains(toggled)
                            ? Note.Of("Ignored in the next run · click to return it")
                            : Note.Of("Click to ignore it in the next run · right-click to run only it"),
                        en
                    ),
                TextStyle.Small
            );
            _hover.ShowAt(_canvas.GetGlobalTransform() * position, _owner.GetGlobalRect());
            return;
        }
        if (BarAt(position) is not { } bar || _run == null || _table == null)
        {
            var groups =
                _bandHits.FirstOrDefault(h => h.Rect.HasPoint(position)).Groups
                ?? _bandHits.FirstOrDefault(h => Near(h.Rect, position)).Groups;
            if (groups == null)
            {
                _hover.Hide();
                return;
            }
            var band = groups[0];
            _hover.Reset();
            ForgetEnglish();
            if (groups.Count > 1)
            {
                HoverLine(
                    _hover.Content,
                    en =>
                        Words.Render(
                            Note.Plural(
                                "{0} area without results",
                                "{0} areas without results",
                                groups.Count,
                                groups.Count
                            ),
                            en
                        ),
                    TextStyle.BodyStrong
                );
                HoverLine(_hover.Content, _ => string.Join(", ", groups.Select(g => g.Name)), TextStyle.Small);
            }
            else
            {
                HoverLine(_hover.Content, _ => band.Name, TextStyle.BodyStrong);
            }
            if (band.AllEmpty)
            {
                var bars = groups.SelectMany(g => g.Bars).ToList();
                if (groups.Count == 1)
                    HoverLine(
                        _hover.Content,
                        en =>
                            Words.Render(
                                Note.Plural(
                                    "{0} target, no result",
                                    "{0} targets, none with a result",
                                    bars.Count,
                                    bars.Count
                                ),
                                en
                            ),
                        TextStyle.Small
                    );
                // The reason, when every target shares it.
                if (bars.Select(b => b.Rank.Reason).Distinct().Count() == 1 && bars[0].Rank.Reasons != null)
                    HoverLine(_hover.Content, en => RankList.Reason(bars[0].Rank.Reasons, en) ?? "", TextStyle.Small);
            }
            HoverLine(
                _hover.Content,
                en =>
                    Words.Render(
                        band.AllEmpty
                            ? band.Collapsed
                                ? groups.Count > 1
                                    ? Note.Of("Click to show their targets")
                                    : Note.Of("Click to show its targets")
                                : Note.Of("Click to collapse")
                            : band.Folded
                                ? Note.Of("Click to unfold")
                                : Note.Of("Click to fold into its best bar"),
                        en
                    ),
                TextStyle.Tiny
            );
            _hover.ShowAt(_canvas.GetGlobalTransform() * position, _owner.GetGlobalRect());
            return;
        }
        _hover.Reset();
        ForgetEnglish();
        var table = _table;
        var head = Ui.Add(_hover.Content, Ui.Row(10));
        head.AddChild(Slot.Make(bar.Portrait, 48));
        var names = Ui.Add(head, Ui.Column(0));
        HoverLine(names, _ => bar.Name, TextStyle.BodyStrong);
        HoverLine(names, en => en ? bar.AreaEnglish : bar.Area, TextStyle.Small);
        if (bar.Rank.Rank is { } rank)
            HoverLine(names, en => Words.Render(Note.Of("#{0} of {1}", rank, table.Count), en), TextStyle.Gold);
        else if (bar.Rank.Died)
            HoverLine(names, en => Words.Render(Note.Of("Died every fight"), en), TextStyle.Small);
        if (bar.Rank.Estimate is { } e)
        {
            var result = _run.Results.GetValueOrDefault(bar.Key);
            var prices = PanelState.Prices();
            var metric = Metrics.Describe(table.Metric, PanelState.Names());
            var unit = _unit;
            HoverLine(
                _hover.Content,
                en =>
                    Words.Render(
                        Note.Of(
                            "{0}: {1}  ({2})",
                            Words.MetricName(metric, unit, en),
                            Ui.Number(e.Point, en),
                            Ui.Range(e.Low, e.High, en)
                        ),
                        en
                    ),
                TextStyle.Body
            );
            HoverLine(
                _hover.Content,
                en => Words.Render(Note.Of("Deaths: {0}", Ui.Percent(bar.DeathRate, en)), en),
                TextStyle.Small
            );
            if (result != null && Metrics.Estimate(result, Metrics.Kills, TimeUnit.Hour, prices) is { } kills)
                HoverLine(
                    _hover.Content,
                    en => Words.Render(Note.Of("Kills / h: {0}", Ui.Number(kills.Point, en)), en),
                    TextStyle.Small
                );
            if (result is { RanOver: > 0 })
                _hover.Content.AddChild(RanOverChip.Box(result));
            if (result != null && Metrics.Estimate(result, Metrics.KillTime, TimeUnit.Hour, prices) is { } time)
                HoverLine(
                    _hover.Content,
                    en => Words.Render(Note.Of("Time per kill: {0} s", Ui.Number(time.Point, en)), en),
                    TextStyle.Small
                );
        }
        else
        {
            HoverLine(
                _hover.Content,
                en => RankList.Reason(bar.Rank.Reasons, en) ?? Words.Render(Note.Of("no result"), en),
                TextStyle.Small
            );
        }
        if (bar.Folded)
            HoverLine(_hover.Content, en => Words.Render(Note.Of("Best of its folded area"), en), TextStyle.Tiny);
        HoverLine(_hover.Content, en => Words.Render(Note.Of("Click: details · Shift-click: pin"), en), TextStyle.Tiny);
        _hover.ShowAt(_canvas.GetGlobalTransform() * position, _owner.GetGlobalRect());
    }

    public void HideHover() => _hover.Hide();

    // Dev builds only: the hover's lines in English, for cs2dev chart's reply.
    partial void KeepEnglish(Func<bool, string> text);

    partial void ForgetEnglish();
}
