using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// One side as something to look at: what it starts from, its gear where the game's equipment screen puts it, its
/// style, food, prayers and spell, how many changes it carries, and whether its requirements are checked. What it
/// shows is the side after its changes, read from the side's check, never the live character alone.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class SetupCard
{
    private readonly SideLetter _letter;
    private readonly PanelContainer _root;
    private readonly Label _name;
    private readonly GridContainer _gear;
    private readonly CardLines _facts;
    private readonly Label _changes;
    private readonly Label _note;
    private readonly Control _body;
    private readonly Control _lines;
    private readonly Control _linesTop;
    private readonly PanelContainer _mark;
    private readonly Button _clear;
    private readonly Button _bypass;
    private readonly HBoxContainer _actions;
    private readonly Button _change;
    private readonly Button _edit;
    private readonly Button _variant;
    private const float ShortcutSize = 26;

    // What the lines beside the grid keep for themselves before the grid grows; they clip their ends past it.
    private const float LinesRoom = 120;

    // Ignore requirements is a quiet line at the card's foot: its box smaller than a settings tick.
    private const float BypassBox = 16;

    private readonly Button _copy;
    private readonly PresetNaming _naming;

    private SideCheck? _check;
    private SideState _side = SideState.AsNow;
    private string _gearShown = "";
    private string _gearDrawn = "";
    private string _stateShown = "checking";

    // The slots' size for the card's width, and the gear last shown, drawn again at a new size.
    private float _slot = MiniSlot.Size;
    private (SetupState State, SetupState Before)? _shownGear;

    public SetupCard(SideLetter letter)
    {
        _letter = letter;
        var suffix = letter == SideLetter.A ? "A" : "B";
        _root = Look.Panel(Art.RowBox, "Cs2Card" + suffix);
        var column = Ui.Add(_root, Ui.Column(6));

        // The header: the side's letter (once there are two), who it is and what it starts from, and Change.
        var head = Ui.Add(column, Ui.Row(8));
        _mark = new PanelContainer { Name = "Cs2CardLetter" + suffix };
        // Close around the letter: the box's own padding is for a line of text.
        var badge = (StyleBox)Look.Box(Art.BadgeBox).Duplicate();
        badge.ContentMarginLeft = badge.ContentMarginRight = 8;
        badge.ContentMarginTop = badge.ContentMarginBottom = 1;
        PixelBox.Panel(_mark, badge);
        _mark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var letterText = Ui.Label(suffix, TextStyle.Label);
        letterText.LabelSettings = SideLine.Gold(TextStyle.Label);
        letterText.HorizontalAlignment = HorizontalAlignment.Center;
        _mark.AddChild(letterText);
        head.AddChild(_mark);
        _name = Ui.Clipped("", TextStyle.Label);
        _name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(_name);
        _change = Ui.Button(Tr.Get("Change"), Look.Button4, "Cs2CardChange", () => ChangePressed?.Invoke(_letter));
        _change.TooltipText = Tr.Get("Pick what this side starts from");
        Compact(_change);
        head.AddChild(_change);

        // The body: the gear as large as the card's width allows, and beside it what else the side brings.
        _body = Ui.Add(column, Ui.Row(12));
        _gear = Ui.Add(_body, new GridContainer { Name = "Cs2CardGear" });
        // Edge to edge: each slot keeps its own space around its box.
        _gear.AddThemeConstantOverride("h_separation", 0);
        _gear.AddThemeConstantOverride("v_separation", 0);
        _gear.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var side = Ui.Add(_body, Ui.Column(4));
        side.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // The lines start a slot down, the style level with the ammo slot: the grid's top row holds the helmet alone.
        _linesTop = Ui.Add(side, new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        _linesTop.CustomMinimumSize = new Vector2(0, _slot);
        var lines = Ui.Add(side, Ui.Column(2));
        _lines = lines;
        _facts = new CardLines(lines, key => SlotPressed?.Invoke(_letter, key));
        var changed = Ui.Add(lines, Ui.Row(8));
        _changes = Ui.Add(changed, Ui.Label("", TextStyle.Gold, "Cs2CardChanges"));
        _changes.MouseFilter = Control.MouseFilterEnum.Stop;
        _clear = Ui.Button(
            Tr.Get("Clear"),
            Look.Button4,
            "Cs2CardClear" + suffix,
            () => PanelState.SetSide(_letter, _side.WithChanges(ChangeList.Empty))
        );
        _clear.Flat = true;
        _clear.TooltipText = Tr.Get("Undo every change");
        changed.AddChild(_clear);
        _body.Resized += FitGear;

        _note = Ui.Add(column, Ui.Label("", TextStyle.Tiny, wrap: true));

        // The foot: Ignore requirements, a quiet line in the small text, then the shortcuts and Edit.
        _actions = Ui.Add(column, Ui.Row(8));
        _bypass = Cs2Check.Make(
            "Cs2CardBypass" + suffix,
            Tr.Get("Ignore requirements"),
            toggled: on => RequirementsChosen?.Invoke(_letter, on)
        );
        _bypass.TooltipText = Dependencies.IsAvailable(Dependencies.IgnoreRequirements)
            ? Tr.Get("Ignore level, quest and unlock requirements of targets, gear and Setup entries.")
            : Tr.Get(
                "Ignore level, quest and unlock requirements of targets. Gear and Setup entries: unsupported on this game build."
            );
        Cs2Check.Small(_bypass, BypassBox);
        _bypass.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _bypass.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _actions.AddChild(_bypass);
        _edit = Ui.Button(Tr.Get("Edit"), Look.Button4, "Cs2CardEdit" + suffix, () => EditPressed?.Invoke(_letter));
        Compact(_edit);
        _edit.TooltipText = Tr.Get("Levels, gear, prayers, guilds and more, on the Setup tab");
        _actions.AddChild(_edit);

        // Only on A while it stands alone: B is made from A, and a second side has nothing of its own to copy into.
        _variant = IconButton.Quiet(
            Art.File(Art.DuplicatePath),
            Tr.Format("Compare variant: copy this setup to {0}", SideLine.B),
            "Cs2CardVariant" + suffix,
            () => VariantPressed?.Invoke()
        );
        // The two shortcuts as one small pair before Edit, smaller than the buttons so they read as extras.
        var shortcuts = Ui.Row(0, "Cs2CardShortcuts" + suffix);
        shortcuts.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _actions.AddChild(shortcuts);
        _actions.MoveChild(shortcuts, _edit.GetIndex());
        shortcuts.AddChild(_variant);
        // The side as one line for another player, as the Setup tab's Export setup.
        _copy = IconButton.Quiet(
            Ui.Greyscale(Art.File(Art.CopyPath)),
            Tr.Get("Export setup: copies this side as one line, for another player to import"),
            "Cs2CardCopy" + suffix,
            CopySetup
        );
        shortcuts.AddChild(_copy);
        _naming = PresetNaming.WithIcon(suffix, () => _side.Changes, shortcuts, column, ShortcutSize);
        foreach (var icon in new[] { _variant, _copy })
            icon.CustomMinimumSize = new Vector2(ShortcutSize, ShortcutSize);
        _variant.AddThemeConstantOverride("icon_max_width", 18);
        // The game's copy picture fills its square, where the variant's leaves a margin: drawn smaller to match.
        _copy.AddThemeConstantOverride("icon_max_width", 15);
    }

    // Exports the side as last checked; says so over the icon, which moves nothing.
    private void CopySetup()
    {
        if (_check != null)
            FloatNote.Show(_copy, Sharing.ExportSide(_check) ?? Sharing.Exported);
    }

    /// <summary>How far the card's box keeps its buttons from its right edge.</summary>
    public static int RightInset => (int)Look.Box(Art.RowBox).GetMargin(Side.Right);

    /// <summary>A card's button small, in the small text: the card's own lines are what it is for.</summary>
    public static void Compact(Button button)
    {
        Look.Narrow(button, 8);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        void Size()
        {
            button.AddThemeFontSizeOverride("font_size", Look.Style(TextStyle.Small).FontSize);
        }
        Look.OnTheme(button, Size);
        Size();
    }

    public Control Root => _root;

    public event Action<SideLetter>? ChangePressed;

    /// <summary>
    /// Raised when a slot of the gear grid is clicked, with the slot's id, or one of the lines beside it, with the
    /// line's key (<see cref="SlotPicker.StanceLine"/> and the like).
    /// </summary>
    public event Action<SideLetter, string>? SlotPressed;

    /// <summary>Raised when a slot is right-clicked: it steps down once, as on the Setup tab.</summary>
    public event Action<SideLetter, string>? SlotStepped;

    /// <summary>The card's Change button, which the base picker opens level with.</summary>
    public Control ChangeButton => _change;

    /// <summary>The control drawn for <paramref name="slot"/> in the gear grid, or for a line's key; null when it isn't drawn.</summary>
    public Control? Slot(string slot) =>
        _facts.Row(slot) ?? _gear.FindChild(Ui.Id("Cs2CardSlot", slot), false, false) as Control;

    public event Action<SideLetter>? EditPressed;

    /// <summary>Raised when Compare variant is pressed: the side is copied whole to side B.</summary>
    public event Action? VariantPressed;

    public event Action<SideLetter, bool>? RequirementsChosen;

    /// <summary>The gear as <c>cs2 ui</c> prints it: the items in slot order; empty before the side is read.</summary>
    public string GearLine => _gearShown;

    /// <summary>
    /// Where the card is, for <c>cs2 ui</c>: <c>checked</c> once it shows its side as it is now, <c>checking</c>
    /// while the side is read (again), <c>saved-run</c> for a saved run, which has nothing to read.
    /// </summary>
    public string State => _stateShown;

    /// <summary>
    /// Shows <paramref name="side"/>. <paramref name="check"/> is its latest check (null before the first),
    /// <paramref name="pending"/> whether a newer one is being made: the card then keeps what it shows, faded.
    /// <paramref name="comparing"/> is whether there are two cards, <paramref name="locked"/> whether a run is going
    /// (nothing can be changed then), <paramref name="canEdit"/> whether its changes can be edited from the card,
    /// <paramref name="saved"/> the saved run when the side is one.
    /// </summary>
    public void Show(
        SideState side,
        SideCheck? check,
        bool pending,
        string character,
        string? loadoutName,
        bool comparing,
        bool locked,
        bool canEdit,
        RunHeader? saved
    )
    {
        var names = PanelState.Names();
        var isRun = side.Base.Kind == SideBaseKind.SavedRun;
        var start = side.Base.Kind switch
        {
            // The character itself needs no line: with changes it isn't "as it is now" either.
            SideBaseKind.Current => "",
            SideBaseKind.Loadout => loadoutName ?? Tr.Get("A loadout that is gone"),
            _ => saved == null
                ? Tr.Get("A saved run that is gone")
                : Tr.Format(
                    "Saved run · {0}",
                    (saved.EndedAt ?? saved.StartedAt).ToLocalTime().ToString("g", Tr.Culture)
                ),
        };
        // Who the side is and what it starts from, on one line: the header keeps to the height of its buttons.
        var who = isRun ? saved?.Name ?? side.Base.Id! : character;
        Ui.SetClipped(_name, start.Length > 0 ? who + " · " + start : who);
        _side = side;
        _mark.Visible = comparing;
        _body.Visible = !isRun;
        _actions.Visible = !isRun;
        _note.Visible = isRun;
        _note.Text = Tr.Get(
            "Its results are shown as they were saved. A saved run keeps no setup, so it has no gear to show, nothing to change and no stats."
        );
        Pending.Show(_gear, pending && !isRun);
        Pending.Show(_lines, pending && !isRun);
        _edit.Visible = canEdit;
        _change.Disabled = locked;
        _edit.Disabled = locked;
        _variant.Visible = _letter == SideLetter.A && !comparing && canEdit;
        _variant.Disabled = locked;
        _check = isRun ? null : check;
        // The line is the side as its check found it: nothing to copy before the check is in.
        _copy.Disabled = check == null || pending;
        _naming.Allow(!side.Changes.IsEmpty && !isRun);
        Cs2Check.Set(_bypass, side.IgnoreRequirements);
        _bypass.Disabled = locked;
        _changes.Visible = !side.Changes.IsEmpty;
        _clear.Visible = !side.Changes.IsEmpty;
        _clear.Disabled = locked;
        _changes.Text = Tr.Plural("{0} change", "{0} changes", side.Changes.Count, side.Changes.Count);
        // Each change on hover, as the Setup tab's changes bar words it.
        var view = check != null ? new SetupTab.SideView(check.Changes, side.Changes) : null;
        _changes.TooltipText = string.Join(
            "\n",
            side.Changes.Select(change =>
            {
                var from = view?.Outcome(change)?.From ?? view?.From(change);
                return Tr.Note(ChangeNotes.Describe(change, from, names));
            })
        );
        _stateShown =
            isRun ? "saved-run"
            : check == null || pending ? "checking"
            : "checked";
        if (isRun || check == null)
        {
            _gearShown = "";
            _shownGear = null;
            if (isRun)
            {
                Ui.Clear(_gear);
                _gearDrawn = "";
            }
            else
            {
                // Nothing read yet for this side: said in the lines that are there, beside the grid with its
                // slots empty, so all of it keeps its place when the side arrives.
                EmptyGear();
                _facts.Checking();
            }
            return;
        }
        var state = check.Changes.After;
        var gear = check.Changes.Gear;
        _facts.Show(state, gear, names);
        _shownGear = (state, check.Changes.Before);
        ShowGear(state, check.Changes.Before, names);
    }

    // The slots as large as the card's width allows beside lines that stay readable, between the game's own size
    // and the largest; the grid is drawn again when that changes.
    private void FitGear()
    {
        var room = (_body.Size.X - LinesRoom) / 3;
        var size = Math.Clamp(MathF.Floor(room), MiniSlot.Size, MiniSlot.MaxSize);
        if (size == _slot)
            return;
        _slot = size;
        _linesTop.CustomMinimumSize = new Vector2(0, size);
        _gearDrawn = "";
        if (_shownGear is { } gear)
            ShowGear(gear.State, gear.Before, PanelState.Names());
        else
        {
            Ui.Clear(_gear);
            EmptyGear();
        }
    }

    // The grid's shape before a side has been read: every slot there, holding nothing.
    private void EmptyGear()
    {
        var grid = ChangeOptions.Grid();
        if (grid.Count == 0 || _gear.GetChildCount() > 0)
            return;
        // The empty slots stand for no gear read: the same gear read again must be drawn over them.
        _gearDrawn = "";
        _gear.Columns = grid.Max(g => g.Column) + 1;
        for (var row = 0; row <= grid.Max(g => g.Row); row++)
        for (var column = 0; column < _gear.Columns; column++)
            _gear.AddChild(
                grid.FirstOrDefault(g => g.Column == column && g.Row == row) is { } cell
                    ? MiniSlot.Make("", ChangeOptions.Icon(cell.Slot), true, false, "", size: _slot)
                    : new Control { CustomMinimumSize = new Vector2(_slot, _slot) }
            );
    }

    // The slots where the game's equipment screen puts them; rebuilt only when what they hold changed.
    private void ShowGear(SetupState state, SetupState before, Func<string, string> names)
    {
        var key = string.Join('|', state.Slots.Select(s => s.Slot + "=" + s.Item));
        var changedKey = key + "#" + string.Join('|', before.Slots.Select(s => s.Item));
        _gearShown = string.Join(',', state.Slots.Select(s => s.Item is { } i ? Ids.Brief(i) : "none"));
        if (changedKey == _gearDrawn)
            return;
        _gearDrawn = changedKey;
        Ui.Clear(_gear);
        var grid = ChangeOptions.Grid();
        if (grid.Count == 0)
            return;
        var columns = grid.Max(g => g.Column) + 1;
        var rows = grid.Max(g => g.Row) + 1;
        _gear.Columns = columns;
        var was = before.Slots.ToDictionary(s => s.Slot, s => s.Item, StringComparer.Ordinal);
        var held = state.Slots.ToDictionary(s => s.Slot, s => s.Item, StringComparer.Ordinal);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (grid.FirstOrDefault(g => g.Column == column && g.Row == row) is not { } cell)
                {
                    _gear.AddChild(new Control { CustomMinimumSize = new Vector2(_slot, _slot) });
                    continue;
                }
                var item = held.GetValueOrDefault(cell.Slot);
                var slotName = names(cell.Slot);
                var changed = was.GetValueOrDefault(cell.Slot) != item;
                _gear.AddChild(
                    MiniSlot.Make(
                        Ui.Id("Cs2CardSlot", cell.Slot),
                        ChangeOptions.Icon(item ?? cell.Slot),
                        item == null,
                        changed,
                        (
                            item == null
                                ? Tr.Format("{0}: empty", slotName)
                                : Tr.Format("{0}: {1}", slotName, names(item))
                        )
                            + (
                                changed ? "\n" + Marks.Muted(Tr.Get("Right-click: put back"))
                                : item != null ? "\n" + Marks.Muted(Tr.Get("Right-click: empty"))
                                : ""
                            ),
                        () => SlotPressed?.Invoke(_letter, cell.Slot),
                        () => SlotStepped?.Invoke(_letter, cell.Slot),
                        _slot
                    )
                );
            }
        }
    }
}
