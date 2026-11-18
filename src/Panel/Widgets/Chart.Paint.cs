using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

// Painting the chart: axis, grid, area bands, bars, ranges and portraits, an ignored target's crossed out.
internal sealed partial class Chart
{
    private void Paint()
    {
        _barHits.Clear();
        _bandHits.Clear();
        _toggleHits.Clear();
        var font = Look.Font(400);
        var text = Look.Text(_canvas);
        var muted = Look.Muted(_canvas);
        var plot = Plot();
        _canvas.PxRect(new Rect2(Vector2.Zero, _canvas.Size), Colors.Black with { A = 0.25f });
        if (_table == null || _groups.Count == 0)
        {
            _canvas.DrawString(
                font,
                new Vector2(20, 40),
                Tr.Get("Nothing to chart yet."),
                HorizontalAlignment.Left,
                -1,
                16,
                muted
            );
            return;
        }
        var top = ShownTop > 0 ? ShownTop : NiceTop(_max);
        for (var i = 0; i <= 4; i++)
        {
            var value = top * i / 4;
            var y = plot.End.Y - (float)(i / 4.0) * plot.Size.Y;
            _canvas.PxLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), muted with { A = 0.15f }, 1);
            _canvas.DrawString(
                font,
                new Vector2(4, y + 5),
                Ui.Number(value),
                HorizontalAlignment.Right,
                AxisWidth - 10,
                12,
                muted
            );
        }

        var x = plot.Position.X + 4;
        var portraits = new List<(Texture2D? Art, Rect2 Rect, bool OnTop, bool Ignored)>();
        var arts = new List<(Texture2D Art, Rect2 Rect)>();
        foreach (var column in Columns())
        {
            var group = column[0];
            var start = x;
            if (group.Collapsed)
            {
                // A stub: a short outline at the base standing for the areas, with their count when several.
                var width = StubWidth(column.Count);
                _canvas.PxRect(new Rect2(x + 3, plot.End.Y - 6, width - 6, 6), muted with { A = 0.6f }, false, 1);
                if (column.Count > 1)
                    _canvas.DrawString(
                        font,
                        new Vector2(x, plot.End.Y - 10),
                        Ui.Number(column.Count),
                        HorizontalAlignment.Center,
                        width,
                        12,
                        muted
                    );
                x += width;
            }
            else
            {
                // A target drawn as its own area (a dungeon): the band below already shows its art.
                var own = group.Bars.Count == 1 && ReferenceEquals(group.Bars[0].Portrait, group.Art);
                foreach (var bar in group.Bars)
                {
                    PaintBar(bar, x, plot, top, font, muted, portraits, own);
                    x += SlotOf(bar);
                }
            }
            // The area's band: art, name, and the click target that folds it.
            var band = new Rect2(start, plot.End.Y + PortraitRow + 4, Math.Max(EmptySlot, x - start) - 2, BandRow - 8);
            _canvas.PxRect(band, Colors.White with { A = group.Folded || group.Collapsed ? 0.10f : 0.06f });
            _canvas.PxRect(band, Colors.White with { A = 0.15f }, false, 1);
            // The art keeps a readable size; in a band too narrow for it, it sits centred and overlaps the edges.
            var art = Math.Min(band.Size.Y - 6, Math.Max(IconMin, band.Size.X - 4));
            var nameWidth = band.Size.X - art - 10;
            // The name only when it fits whole; a cut name says less than the art, and hover gives it.
            var nameFits =
                !group.Collapsed
                && nameWidth > 0
                && font.GetStringSize(group.Name, HorizontalAlignment.Left, -1, 12).X <= nameWidth;
            if (group.Collapsed)
            {
                // The collapsed areas' art, stacked like cards, the first on top.
                var stacked = column.Take(StackShown).Where(g => g.Art != null).ToList();
                var left = band.GetCenter().X - (IconMin + (stacked.Count - 1) * StackStep) / 2;
                for (var i = stacked.Count - 1; i >= 0; i--)
                    arts.Add(
                        (
                            stacked[i].Art!,
                            new Rect2(
                                left + i * StackStep,
                                band.Position.Y + (band.Size.Y - IconMin) / 2,
                                IconMin,
                                IconMin
                            )
                        )
                    );
            }
            else if (group.Art != null)
            {
                var artX = nameFits ? band.Position.X + 3 : band.GetCenter().X - art / 2;
                arts.Add((group.Art, new Rect2(artX, band.Position.Y + (band.Size.Y - art) / 2, art, art)));
            }
            if (nameFits)
                _canvas.DrawString(
                    font,
                    band.Position + new Vector2(art + 7, band.Size.Y / 2 + 5),
                    group.Name,
                    HorizontalAlignment.Left,
                    nameWidth,
                    12,
                    text,
                    TextServer.JustificationFlag.None
                );
            // A stub's whole column folds and unfolds, as it has no bar to hover.
            var hit = group.Collapsed
                ? new Rect2(start, plot.Position.Y, x - start, band.End.Y - plot.Position.Y)
                : band;
            _bandHits.Add((hit, column));
            x += 4;
        }
        // Icons last, so one that overlaps a neighbour isn't painted over; the selected one on top.
        foreach (var (texture, rect) in arts)
            _canvas.DrawTextureRect(texture, rect, false);
        foreach (var (texture, rect, _, ignored) in portraits.OrderBy(p => p.OnTop))
        {
            if (texture != null)
                _canvas.DrawTextureRect(texture, rect, false, ignored ? Colors.White with { A = 0.4f } : Colors.White);
            if (ignored)
                Cross(rect.Grow(-2), Look.Deadly);
        }

        ChartMarks.Legend(_canvas, font, new Vector2(plot.Position.X, _canvas.Size.Y - LegendRow + 16), muted);
    }

    private void PaintBar(
        BarItem bar,
        float x,
        Rect2 plot,
        double top,
        Font font,
        Color muted,
        List<(Texture2D?, Rect2, bool, bool)> portraits,
        bool ownArea
    )
    {
        var slot = SlotOf(bar);
        var to = _canvas.GetViewport().GetFinalTransform() * _canvas.GetGlobalTransformWithCanvas();
        var (left, width, frameLeft, frameWidth, line, edge) = BarSlot.Of(to.Origin.X, to.X.Length(), x, slot);
        var hit = new Rect2(x, plot.Position.Y, slot, plot.Size.Y);
        _barHits.Add((hit, bar));
        var selected = PanelState.Selected == bar.Key;
        var ignored = PanelState.Ignored.Contains(bar.Key);
        var column = new Rect2(frameLeft, plot.Position.Y, frameWidth, plot.Size.Y + PortraitRow);
        // The selection: a faint column behind the bar and a white outline, readable on any bar colour.
        if (selected)
            _canvas.PxRect(column, Colors.White with { A = 0.08f });
        Rect2? drawn = null;
        if (bar.Rank.Died)
        {
            // A value earned while dying draws no bar: a stub with the mark, whatever the value.
            ChartMarks.Stub(_canvas, left, width, plot.End.Y, ignored ? muted with { A = 0.35f } : Look.Deadly);
        }
        else if (bar.Rank.Estimate is { } e && top > 0)
        {
            var shown = Shown(bar.Key);
            var point = double.IsFinite(e.Point) ? shown.Point : top;
            var h = (float)(Math.Clamp(point / top, 0, 1) * plot.Size.Y);
            var rect = new Rect2(left, plot.End.Y - h, width, Math.Max(1, h));
            drawn = rect;
            var colour = bar.Key == _table?.BestKey ? Look.Best : Look.Status(bar.DeathRate);
            _canvas.PxRect(rect, ignored ? muted with { A = 0.35f } : colour);
            if (double.IsFinite(e.Point) && shown.High > shown.Low)
            {
                var cx = left + width / 2;
                var y1 = plot.End.Y - (float)(Math.Clamp(shown.Low / top, 0, 1) * plot.Size.Y);
                var y2 = plot.End.Y - (float)(Math.Clamp(shown.High / top, 0, 1) * plot.Size.Y);
                var cap = Math.Max(2, width * 0.25f);
                _canvas.PxLine(new Vector2(cx, y1), new Vector2(cx, y2), Colors.White, 1.5f);
                _canvas.PxLine(new Vector2(cx - cap, y2), new Vector2(cx + cap, y2), Colors.White, 1.5f);
                _canvas.PxLine(new Vector2(cx - cap, y1), new Vector2(cx + cap, y1), Colors.White, 1.5f);
            }
            if (bar.Folded)
                _canvas.PxRect(rect.Grow(edge / 2), Look.Best, false, edge);
        }
        else
        {
            // No estimate: a short outline at the base, with the reason on hover.
            var colour = bar.Rank.IsRunning ? Look.Running : muted;
            _canvas.PxRect(new Rect2(left, plot.End.Y - 6, width, 6), colour, bar.Rank.IsRunning);
            if (slot >= 18)
                _canvas.DrawString(
                    font,
                    new Vector2(x, plot.End.Y - 10),
                    bar.Rank.IsRunning ? "…" : "–",
                    HorizontalAlignment.Center,
                    slot,
                    12,
                    colour
                );
        }
        if (selected)
            _canvas.PxRect(column.Grow(-line / 2), Colors.White with { A = 0.85f }, false, line);
        ChartMarks.RanOver(_canvas, ignored ? null : drawn, bar.Rank.RanOver);
        if (PanelPins.ShownPins.Contains(bar.Key))
            ChartMarks.Pip(_canvas, new Vector2(x + slot / 2, plot.Position.Y + 4));
        // The portrait keeps a readable size, overlapping its neighbours slightly when the slot is narrower.
        if (!bar.Empty && bar.Portrait != null)
        {
            var size = Math.Clamp(slot - 2, IconMin, PortraitRow - 4);
            // Empty when the band shows the same art; it still toggles, and shows the cross when ignored.
            var rect = new Rect2(x + (slot - size) / 2, plot.End.Y + 3, size, size);
            portraits.Add((ownArea ? null : bar.Portrait, rect, selected, ignored));
            _toggleHits.Add((new Rect2(x, plot.End.Y, slot, PortraitRow), bar.Key));
        }
    }

    private void Cross(Rect2 rect, Color colour)
    {
        _canvas.PxLine(rect.Position, rect.End, colour, 2);
        _canvas.PxLine(new Vector2(rect.End.X, rect.Position.Y), new Vector2(rect.Position.X, rect.End.Y), colour, 2);
    }
}
