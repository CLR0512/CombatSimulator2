using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A long list in which only the rows on screen exist: every row has one height, and scrolling hands the same few
/// row controls the items now in view. Building or scrolling a list of hundreds costs what its visible rows cost.
/// </summary>
internal sealed class VirtualRows
{
    private readonly float _rowHeight;
    private readonly Func<Control> _make;
    private readonly Action<Control, int> _bind;
    private readonly Control _body;
    private readonly List<(Control Row, int Index)> _rows = [];
    private int _count;

    public ScrollContainer Root { get; }

    /// <summary>
    /// A list named <paramref name="name"/> whose rows are <paramref name="rowHeight"/> tall. <paramref name="make"/>
    /// builds an empty row; <paramref name="bind"/> fills a row with the item at an index.
    /// </summary>
    public VirtualRows(string name, float rowHeight, Func<Control> make, Action<Control, int> bind)
    {
        _rowHeight = rowHeight;
        _make = make;
        _bind = bind;
        Root = new ScrollContainer
        {
            Name = name,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _body = Ui.Add(
            Root,
            new Control
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Pass,
            }
        );
        Root.GetVScrollBar().ValueChanged += _ => Place(rebind: false);
        Root.Resized += () => Place(rebind: false);
        // The body takes its width after the list does: rows placed before then would keep a width of nothing.
        _body.Resized += () => Place(rebind: false);
        Root.GetVScrollBar().VisibilityChanged += () => Place(rebind: false);
    }

    /// <summary>How many rows are built: those in view and one more.</summary>
    public int Built => _rows.Count;

    /// <summary>Shows <paramref name="count"/> items, filling every row in view again.</summary>
    public void SetCount(int count)
    {
        _count = Math.Max(0, count);
        Place(rebind: true);
    }

    /// <summary>Scrolls so the item at <paramref name="index"/> is in view.</summary>
    public void ScrollTo(int index) =>
        Root.ScrollVertical = (int)(Math.Clamp(index, 0, Math.Max(0, _count - 1)) * Step());

    // The row height in whole screen pixels: rows meeting on a fraction of a pixel draw their shared edge one pixel
    // thick at some joins and two at others.
    private float Step()
    {
        var scale = (Root.GetViewport().GetFinalTransform() * Root.GetGlobalTransformWithCanvas()).Scale.Y;
        return scale > 0 ? Mathf.Max(1, Mathf.Round(_rowHeight * scale)) / scale : _rowHeight;
    }

    // Gives each built row the item now at its place; a row keeps its item when that hasn't changed.
    private void Place(bool rebind)
    {
        var step = Step();
        _body.CustomMinimumSize = new Vector2(0, _count * step);
        var first = (int)(Root.ScrollVertical / step);
        // Rows stop short of the scrollbar, so what they end with stays clear of it.
        var bar = Root.GetVScrollBar();
        var width = _body.Size.X - (bar.Visible ? bar.Size.X + 6 : 0);
        var inView = Math.Min(_count - first, (int)Math.Ceiling(Root.Size.Y / step) + 1);
        while (_rows.Count < inView)
        {
            var row = _make();
            _body.AddChild(row);
            _rows.Add((row, -1));
        }
        for (var i = 0; i < _rows.Count; i++)
        {
            var (row, shown) = _rows[i];
            var index = first + i;
            row.Visible = i < inView;
            if (!row.Visible)
            {
                _rows[i] = (row, -1);
                continue;
            }
            row.Position = new Vector2(0, index * step);
            row.Size = new Vector2(width, step);
            if (rebind || shown != index)
            {
                _bind(row, index);
                _rows[i] = (row, index);
            }
        }
    }
}
