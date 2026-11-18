using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// The what-if editor's picker: every choice the live game's data has for a gear slot, the food, a prayer slot, the
/// spell or the imbuement, searchable by name. Owned choices come first; a choice whose requirements are unmet says
/// which. Item lists show what the character owns or wears until the Unowned tick
/// lets in the rest, marked unowned. The rows are made once as the list opens and the search only hides them, so
/// typing stays within a frame however many items fit a slot. Main thread.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class ItemPicker
{
    private const float Width = 420;
    private const float CompactWidth = 280;
    private readonly bool _compact;

    private readonly PanelContainer _root;
    private readonly Label _title;
    private readonly SideLine _now = new(TextStyle.Small);
    private readonly LineEdit _search;
    private readonly VBoxContainer _rows;
    private readonly Control _footer;
    private readonly Button _unownedTick;
    private bool _items;

    // Shared by both pickers and kept while the game runs: a player looking past their bank keeps doing so.
    private static bool _unowned;

    private static readonly Color UnownedNote = Color.FromHtml("#9a968e");
    private IReadOnlyList<ChangeOption> _options = [];
    private readonly List<(Button Row, string? Name)> _made = [];
    private string? _noneText;
    private Action<string>? _picked;
    private bool _dragging;

    /// <summary>Raised as the title bar is dragged, with how far; when the drag ends; and when the place is reset.</summary>
    public event Action<Vector2>? Dragged;

    public event Action? DragEnded;

    public event Action? PlaceReset;

    // The item the choice would replace, for the change in a row's stats.
    private string? _worn;

    /// <summary>
    /// The picker; <paramref name="compact"/> is the small one that opens under a slot and stays there: narrower
    /// and shorter, with the slot's name and the list alone, and no title bar to drag.
    /// </summary>
    public ItemPicker(bool compact = false)
    {
        _compact = compact;
        var panel = compact ? FlatPanel() : Look.Panel(Art.SideBox, "Cs2ItemPickerPanel");
        _root = NativeStyles.Raised(compact ? "Cs2SlotPicker" : "Cs2ItemPicker", panel);
        _root.CustomMinimumSize = new Vector2(compact ? CompactWidth : Width, 0);
        _root.Visible = false;
        var column = Ui.Add(panel, Ui.Column(8));
        var head = Ui.Add(column, Ui.Row(8));
        // The title bar moves the picker: dragged, it follows; a double click or a middle click puts it back.
        if (!compact)
            Draggable(head);
        var titles = Ui.Add(head, Ui.Column(0));
        titles.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _title = Ui.Label("", compact ? TextStyle.Label : TextStyle.Heading);
        titles.AddChild(_title);
        if (!compact)
            titles.AddChild(_now.Root);
        // The small one closes on a click anywhere else, so it has no button for it.
        if (!compact)
            head.AddChild(Ui.CloseButton("Cs2ItemPickerClose", Close));
        _search = new LineEdit { Name = "Cs2ItemSearch", ClearButtonEnabled = true };
        Look.DarkField(_search);
        _search.TextChanged += _ => Search();
        _search.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                Close();
                _search.AcceptEvent();
            }
        };
        column.AddChild(_search);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, compact ? 264 : 420),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        // No space between rows: the pointer is always on one, so the highlight doesn't drop out between two.
        _rows = Ui.Add(scroll, Ui.Column(0, "Cs2ItemRows"));
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var footer = Ui.Add(column, Ui.Row(0, "Cs2ItemFooter"));
        footer.Alignment = BoxContainer.AlignmentMode.End;
        _footer = footer;
        _unownedTick = Cs2Check.Make(
            "Cs2ItemUnowned",
            Tr.Get("Unowned"),
            _unowned,
            on =>
            {
                _unowned = on;
                Fill();
            }
        );
        footer.AddChild(_unownedTick);
    }

    private static ImageTexture? _noArt;

    private static ImageTexture NoArt() =>
        _noArt ??= ImageTexture.CreateFromImage(Image.CreateEmpty(28, 28, false, Image.Format.Rgba8));

    // The small picker is flat and sharp, as the slots it opens under are: a dark box with a thin edge.
    private static PanelContainer FlatPanel()
    {
        var panel = new PanelContainer { Name = "Cs2SlotPickerPanel" };
        var box = new StyleBoxFlat { BgColor = Color.FromHtml("#151412"), BorderColor = Color.FromHtml("#4a463f") };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(6);
        PixelBox.Panel(panel, box);
        return panel;
    }

    // Its rows are the game's flat box for a thing not picked; the item the side has now, and a row pressed, take
    // a square box of CS2's own in a quiet green (the game's picked box is rounded and bright, which the slots it
    // sits under aren't). Null in a skin, or when the game no longer has the box: the row keeps its button look.
    private static StyleBox? FlatRow(string state, bool current)
    {
        if (!Look.IsGameLook || Art.Resource<StyleBox>(Art.DeselectedBoxPath) is not { } plain)
            return null;
        if (current || state is "pressed" or "hover_pressed")
        {
            var picked = new StyleBoxFlat
            {
                BgColor = Look.Safe.Darkened(0.72f),
                BorderColor = Look.Safe.Darkened(0.35f) with { A = 0.6f },
                ContentMarginLeft = plain.ContentMarginLeft,
                ContentMarginRight = plain.ContentMarginRight,
                ContentMarginTop = plain.ContentMarginTop,
                ContentMarginBottom = plain.ContentMarginBottom,
            };
            picked.SetBorderWidthAll(1);
            return picked;
        }
        if (state == "hover" && plain.Duplicate() is StyleBoxFlat lit)
        {
            lit.BgColor = lit.BgColor.Lightened(0.08f);
            return lit;
        }
        return plain;
    }

    private void Draggable(HBoxContainer head)
    {
        head.MouseFilter = Control.MouseFilterEnum.Stop;
        head.MouseDefaultCursorShape = Control.CursorShape.Move;
        head.TooltipText = Tr.Get("Drag to move · double-click or middle-click: reset");
        head.GuiInput += e =>
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }:
                case InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: true }:
                    _dragging = false;
                    PlaceReset?.Invoke();
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                    if (_dragging && !button.Pressed)
                        DragEnded?.Invoke();
                    _dragging = button.Pressed;
                    break;
                case InputEventMouseMotion motion when _dragging:
                    Dragged?.Invoke(motion.Relative);
                    break;
                default:
                    return;
            }
            head.AcceptEvent();
        };
    }

    public Control Root => _root;

    public bool Open => _root.Visible;

    /// <summary>What the picker is open for: a slot id, <c>food</c>, <c>spell</c>… or <c>closed</c>.</summary>
    public string For { get; private set; } = "closed";

    public int Rows => _made.Count(m => m.Row.Visible);

    /// <summary>
    /// Opens over the editor with <paramref name="options"/>. <paramref name="none"/> is the "Empty slot"-like row's
    /// text, or null when the choice can't be empty; <paramref name="picked"/> gets an id or <c>none</c>.
    /// <paramref name="worn"/> is the item the side has there now. <paramref name="items"/> is a list of the game's
    /// items, which the Unowned tick narrows to the owned ones.
    /// </summary>
    public void Show(
        string forWhat,
        string title,
        string now,
        string search,
        string? none,
        IReadOnlyList<ChangeOption> options,
        Action<string> picked,
        string? worn = null,
        bool items = false
    )
    {
        // Shown again for what it is already open on (the side changed under it): the search stays as typed.
        var again = Open && For == forWhat;
        _worn = worn;
        For = forWhat;
        _title.Text = title;
        _now.Show(now);
        _search.PlaceholderText = search;
        if (!again)
            _search.Text = "";
        _noneText = none;
        _options = options;
        _picked = picked;
        _items = items;
        _footer.Visible = items;
        Cs2Check.Set(_unownedTick, _unowned);
        _root.Visible = true;
        Overlays.Opened(this, _root.IsVisibleInTree, Close);
        Fill();
        if (!again)
            _search.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        _root.Visible = false;
        For = "closed";
        _picked = null;
    }

    private void Fill()
    {
        Ui.Clear(_rows);
        _made.Clear();
        if (_noneText != null)
            Add(Row("Cs2ItemNone", null, _noneText, null, null, Look.Safe, Change.None), null);
        var shown = 0;
        foreach (var option in _options)
        {
            var unowned = _items && !option.Owned && !option.WornInA;
            // The item the side has now stays listed, even one only a change gave it.
            if (unowned && !_unowned && option.Id != _worn)
                continue;
            shown++;
            // The side's own pick is marked whatever else is true of it: it is what the side wears in the slot.
            var selected = option.Id == _worn;
            var note =
                selected ? Tr.Get("selected")
                : option.Unmet.Count > 0 ? Tr.Notes(option.Unmet, "; ")
                : option.WornInA ? (PanelState.B is { CanChange: true } ? Tr.Get("in A") : Tr.Get("worn"))
                : option.Owned ? Tr.Get("owned")
                : unowned ? Tr.Get("unowned")
                : null;
            var noteColour =
                selected ? Look.Best
                : option.Unmet.Count > 0 ? Look.Deadly
                : unowned ? UnownedNote
                : Look.Safe;
            Add(
                Row(
                    "Cs2ItemRow" + shown.ToString(CultureInfo.InvariantCulture),
                    option.Id,
                    option.Name,
                    option.Detail,
                    note,
                    noteColour,
                    option.Id
                ),
                option.Name
            );
        }
        Search();
    }

    // A row with no name, the empty choice, is shown only while nothing is typed.
    private void Add(Button row, string? name)
    {
        _rows.AddChild(row);
        _made.Add((row, name));
    }

    private void Search()
    {
        var text = _search.Text.Trim();
        foreach (var (row, name) in _made)
            row.Visible =
                text.Length == 0 || (name != null && name.Contains(text, StringComparison.CurrentCultureIgnoreCase));
    }

    private Button Row(
        string name,
        string? icon,
        string text,
        string? detail,
        string? note,
        Color noteColour,
        string value
    )
    {
        var button = Ui.Button(
            detail == null ? text : text + "\n" + detail,
            _compact ? Look.Button4 : Look.Button8,
            name,
            () => Pick(value)
        );
        button.Alignment = HorizontalAlignment.Left;
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // The side's own pick looks pressed at rest, so the list shows what the slot holds without a hover.
        var current = value == _worn;
        // The gap between rows is drawn inside each row instead: its box stops a pixel short at top and bottom.
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            if (!button.HasThemeStylebox(state, button.ThemeTypeVariation))
                continue;
            var from = current && state is "normal" or "hover" ? "pressed" : state;
            var box = (StyleBox)
                (
                    (_compact ? FlatRow(state, current) : null)
                    ?? button.GetThemeStylebox(from, button.ThemeTypeVariation)
                ).Duplicate();
            if (box is StyleBoxTexture texture)
            {
                texture.ExpandMarginTop = -1;
                texture.ExpandMarginBottom = -1;
            }
            else if (box is StyleBoxFlat flat)
            {
                flat.ExpandMarginTop = -1;
                flat.ExpandMarginBottom = -1;
            }
            button.AddThemeStyleboxOverride(state, box);
        }
        button.ExpandIcon = false;
        button.AddThemeConstantOverride("icon_max_width", _compact ? 22 : 28);
        // A choice the game has no art for keeps the icon's room, so its name lines up with the others.
        if (icon != null)
            button.Icon = ChangeOptions.Icon(icon) is { } art && !ChangeOptions.IsStandIn(art) ? art : NoArt();
        if (note != null)
        {
            var label = Ui.Label(note, TextStyle.Small);
            label.SetAnchorsPreset(Control.LayoutPreset.RightWide);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.OffsetLeft = -200;
            label.OffsetRight = -10;
            label.Modulate = noteColour;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            button.AddChild(label);
        }
        // What the game says about the choice, read when the pointer first reaches its row.
        if (icon != null)
            InstantTooltips.Lazy(
                button,
                () =>
                    ChangeOptions.About(value, _worn) is { Count: > 0 } about
                        ? text + "\n" + string.Join("\n", about.Select(Tr.Note))
                        : ""
            );
        return button;
    }

    private void Pick(string value)
    {
        var picked = _picked;
        Close();
        picked?.Invoke(value);
    }
}
