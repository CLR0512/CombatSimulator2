using System.Text;
using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// The Setup tab, where a side is built: on the left the side being set up (A or B) and the pages; in the middle the
/// page, with the side's changes under it; on the right what they do, the Effect, with the run control at its foot.
/// Nothing here touches the character: every side is read from a copy. Redraws when the sides, their checks or the
/// run change; an idle tab does nothing. Main thread.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class SetupTabView
{
    private const string EditA = "a";
    private const string EditB = "b";

    private readonly MarginContainer _root;
    private readonly Segmented _sides;
    private readonly Button _bypass;
    private SideLetter _letter;
    private readonly PageList _pages;
    private readonly SideLine _title = new(TextStyle.Heading);
    private readonly RunningStrip _running = new();
    private readonly HBoxContainer _titleRow;
    private readonly HBoxContainer _pageTools;
    private readonly SideLine _subtitle = new(TextStyle.Small);
    private readonly Control _gearScroll;
    private readonly GearPage _gear;
    private readonly EntriesPage _entries;
    private readonly MissingPage _missing;
    private readonly Control _missingFiller;
    private readonly ChangesBar _changes;
    private readonly EffectPanel _effect;
    private readonly Control _overlay;
    private readonly ItemPicker _picker;
    private int _version = -1;
    private bool _runChanged = true;

    public SetupTabView()
    {
        _root = new MarginContainer { Name = "Cs2SetupTab" };
        _root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var columns = Ui.Add(_root, Ui.Row(16));

        var left = Ui.Column(8);
        left.CustomMinimumSize = new Vector2(260, 0);
        // Off the window's edge as far as the Simulate tab's setup column is.
        columns.AddChild(Ui.Margin(left, 12, 0, 0, 0));
        _sides = new Segmented(
            "Cs2EditSides",
            [(EditA, SideLine.A, "Cs2EditA"), (EditB, SideLine.B, "Cs2EditB")],
            chosen => PanelState.SetEditing(chosen == EditB ? SideLetter.B : SideLetter.A)
        );
        // Each takes half the column whatever it says: a side gaining changes mustn't widen the column and move the
        // page beside it.
        foreach (var id in new[] { EditA, EditB })
        {
            var button = _sides.Button(id);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.ClipText = true;
            button.ClipContents = true;
        }
        left.AddChild(_sides.Root);
        _bypass = Cs2Check.Make(
            "Cs2Bypass",
            Tr.Get("Ignore requirements"),
            toggled: on =>
            {
                if ((_letter == SideLetter.A ? PanelState.A : PanelState.B) is { } side)
                    PanelState.SetSide(_letter, side.WithIgnoreRequirements(on));
            }
        );
        left.AddChild(Ui.Margin(_bypass, 8, 0, 0, 0));
        _pages = new PageList();
        _pages.Picked += ShowPage;
        left.AddChild(_pages.Root);

        var middle = Ui.Add(columns, Ui.Column(8));
        middle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // The title, and beside it what runs: the Simulate tab's run control isn't on screen here, and the title's
        // row is always there, so the strip coming and going moves nothing.
        _titleRow = Ui.Add(middle, Ui.Row(12));
        var titleRow = _titleRow;
        _title.Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleRow.AddChild(_title.Root);
        _running.Root.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titleRow.AddChild(_running.Root);
        // The subtitle, and on its right what the page sets for all of itself (the Slayer page's goal).
        var subtitleRow = Ui.Add(middle, Ui.Row(12));
        _subtitle.Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _subtitle.Root.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        subtitleRow.AddChild(_subtitle.Root);
        _pageTools = Ui.Add(subtitleRow, Ui.Row(0));
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        // One page shows at a time: Gear and levels in its own scroll, an entries page scrolling its own rows, or the
        // placeholder of a page the build lacks.
        QuietScrollBar.Apply(scroll);
        _gearScroll = ScrollFade.Wrap(scroll);
        middle.AddChild(_gearScroll);
        _picker = new ItemPicker();
        _gear = new GearPage(_picker);
        scroll.AddChild(_gear.Root);
        _entries = new EntriesPage();
        middle.AddChild(_entries.Root);
        _pageTools.AddChild(_entries.SlayerGoal);
        _missing = new MissingPage();
        _missing.Root.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        middle.AddChild(_missing.Root);
        // Under the placeholder, or in a page's place before it is read, so the page keeps its height.
        _missingFiller = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        middle.AddChild(_missingFiller);

        var right = Ui.Add(columns, Look.Panel(Art.SideBox, "Cs2SetupRight"));
        // As wide as the Simulate tab's right column, so switching tabs leaves the right panel's edge in place.
        void Match() => right.CustomMinimumSize = new Vector2(RightColumn.Width, 0);
        Match();
        RightColumn.Changed += Match;
        right.TreeExiting += () => RightColumn.Changed -= Match;
        // The title bar's About icon stands over this panel's left edge here, as over the details on Simulate.
        Help.AboutButton.Follow(right);
        var rightColumn = Ui.Add(right, Ui.Column(10));
        // The side's changes above their effects, so every page has the tab's full height.
        _changes = new ChangesBar();
        rightColumn.AddChild(_changes.Root);
        _effect = new EffectPanel();
        rightColumn.AddChild(_effect.Root);

        // The item picker opens over the page.
        _overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_overlay);
        _overlay.AddChild(_picker.Root);
        _picker.Dragged += MovePicker;
        _picker.DragEnded += PanelState.Save;
        _picker.PlaceReset += ResetPicker;
        RunService.Instance.Changed += RunChanged;
        _root.TreeExiting += () => RunService.Instance.Changed -= RunChanged;
    }

    public Control Root => _root;

    /// <summary>The page shown, for the session: Gear and levels when the tab first opens.</summary>
    public static string Page { get; private set; } = SetupPage.Gear;

    /// <summary>
    /// Opens the tab on <paramref name="letter"/>'s Gear and levels; side B,
    /// when it is asked for and there is none, starts as side A with no changes.
    /// </summary>
    public static void SetUp(SideLetter letter) => SetUp(letter, SetupPage.Gear);

    /// <summary>Opens the tab on <paramref name="letter"/>'s <paramref name="page"/>, as <see cref="SetUp(SideLetter)"/>.</summary>
    public static void SetUp(SideLetter letter, string page)
    {
        if (letter == SideLetter.B && PanelState.B is not { CanChange: true })
            PanelState.SetSide(SideLetter.B, new SideState(PanelState.A.Base, null, PanelState.A.IgnoreRequirements));
        PanelState.SetEditing(letter);
        Page = page;
        Cs2Modal.Jump(Cs2Modal.Setup);
    }

    /// <summary>Shows <paramref name="page"/>.</summary>
    public static void ShowPage(string page)
    {
        Page = page;
        PanelState.Notify();
    }

    private void RunChanged() => _runChanged = true;

    /// <summary>Called every frame while the tab is shown: redraws what changed.</summary>
    public void OnFrame()
    {
        _running.Show();
        // The row keeps the strip's height while it is hidden too.
        _titleRow.CustomMinimumSize = new Vector2(0, _running.Root.GetCombinedMinimumSize().Y);
        if (_picker.Open)
            PlacePicker();
        if (PanelState.Version == _version && !_runChanged)
            return;
        _version = PanelState.Version;
        _runChanged = false;
        Refresh();
    }

    // Where the player dragged it, kept inside the tab; else over the page's middle, near its top.
    private void PlacePicker()
    {
        var size = _picker.Root.GetCombinedMinimumSize();
        _picker.Root.Size = size;
        var prefs = PanelState.Prefs;
        var at = prefs is { PickerX: { } x, PickerY: { } y }
            ? new Vector2((float)x, (float)y)
            : new Vector2((_root.Size.X - size.X) / 2, 60);
        _picker.Root.Position = new Vector2(
            Math.Clamp(at.X, 0, Math.Max(0, _root.Size.X - size.X)),
            Math.Clamp(at.Y, 0, Math.Max(0, _root.Size.Y - size.Y))
        );
    }

    // The picker follows a drag of its title, is saved where it is let go, and goes back on a reset.
    private void MovePicker(Vector2 by)
    {
        var at = _picker.Root.Position + by;
        (PanelState.Prefs.PickerX, PanelState.Prefs.PickerY) = (at.X, at.Y);
        PlacePicker();
    }

    private void ResetPicker()
    {
        (PanelState.Prefs.PickerX, PanelState.Prefs.PickerY) = (null, null);
        PlacePicker();
        PanelState.Save();
    }

    private void Refresh()
    {
        var service = RunService.Instance;
        var locked = service.IsActive;
        var a = PanelState.A;
        var b = PanelState.B is { CanChange: true } other ? other : null;
        var letter = b != null ? PanelState.Editing : SideLetter.A;
        var editing = letter == SideLetter.A ? a : b!;
        var check =
            (letter == SideLetter.A ? PanelState.CheckA : PanelState.CheckB) ?? SideChecker.Latest(letter, editing);
        var name = LiveGame.Find() is { } game ? LiveGame.CharacterName(game) : "";

        _sides.Root.Visible = b != null;
        _sides.Show(letter == SideLetter.B ? EditB : EditA);
        _sides.SetDisabled(locked);
        var changesA = a.Changes.Count;
        var changesB = b?.Changes.Count ?? 0;
        SideLine.OnButton(_sides.Button(EditA), Label(SideLine.A, name, a, changesA));
        SideLine.OnButton(_sides.Button(EditB), b != null ? Label(SideLine.B, name, b, changesB) : SideLine.B);
        _sides.Button(EditA).TooltipText = Tr.Plural("{0} change", "{0} changes", changesA, changesA);
        _sides.Button(EditB).TooltipText = b != null ? Tr.Plural("{0} change", "{0} changes", changesB, changesB) : "";
        _letter = letter;
        Cs2Check.Set(_bypass, editing.IgnoreRequirements);
        // The side is named only when there are two.
        _bypass.TooltipText =
            b == null
                ? Tr.Get("Use any gear and entry, requirements met or not")
                : Tr.Format(
                    "{0} can use any gear and entry, requirements met or not",
                    letter == SideLetter.A ? SideLine.A : SideLine.B
                );
        _bypass.Disabled = locked || !Dependencies.IsAvailable(Dependencies.IgnoreRequirements);
        if (!Dependencies.IsAvailable(Dependencies.IgnoreRequirements))
            _bypass.TooltipText = Tr.Get("Unsupported on this game build");
        _pages.Show(Page, editing.Changes, check?.Pages);

        var side = letter == SideLetter.A ? "A" : "B";
        if (b != null)
            _title.Set(Tr.Get("{0} · {1}"), PageList.Name(Page), side);
        else
            _title.Set(PageList.Name(Page));
        _subtitle.Set(
            letter == SideLetter.B && b!.Base.Equals(a.Base)
                ? Tr.Get("Copy of {0} · simulation only")
                : Tr.Get("Simulation only"),
            "A"
        );
        var page = check?.Pages.FirstOrDefault(p => p.Id == Page);
        var gear = Page == SetupPage.Gear;
        var missing = page is { State: PageState.NoData };
        _gearScroll.Visible = gear;
        _entries.Root.Visible = !gear && !missing && page != null;
        _pageTools.Visible = Page == SetupPage.Slayer && !missing && page != null;
        _missing.Root.Visible = missing;
        // Also while a page waits for the side's first check, so the changes bar doesn't jump to the top.
        _missingFiller.Visible = missing || (!gear && page == null);
        if (gear)
            _gear.Show(letter, editing, check, locked);
        else if (missing)
            _missing.Show(Page);
        else if (page != null)
            _entries.Show(page, letter, editing, check, locked);
        _changes.Show(letter, editing, check, locked, alone: b == null, Page);
        _effect.Show(letter);
        if (locked)
            _picker.Close();
    }

    // "A · Dev" or "B · Loadout2 +3": the side's base and how many changes it has.
    private static string Label(string side, string character, SideState state, int count)
    {
        // A loadout by the name the game gives it ("Loadout 1"), as on its card.
        var from =
            state.Base.Kind != SideBaseKind.Loadout ? character
            : LiveGame.Find() is { } game
            && LiveGame.Loadouts(game).FirstOrDefault(l => l.Slot == state.Base.Id) is { } loadout
                ? loadout.Name
            : Ids.Brief(state.Base.Id!);
        var label = side + " · " + from;
        return count == 0 ? label : label + " +" + count.ToString(Tr.Culture);
    }

    /// <summary>The Effect panel's state, as <c>cs2 ui</c> names it.</summary>
    public string EffectState => _effect.StateKey;

    /// <summary>The tab for <c>cs2 ui</c>: the side and page, the page's state, the Effect, and the item picker.</summary>
    public void Describe(StringBuilder text)
    {
        var letter = PanelState.B is { CanChange: true } ? PanelState.Editing : SideLetter.A;
        var side = letter == SideLetter.A ? PanelState.A : PanelState.B!;
        text.Append("\nediting=")
            .Append(letter == SideLetter.A ? 'a' : 'b')
            .Append(" page=")
            .Append(Page)
            .Append(" page_state=")
            .Append(SideChecker.Checking(letter) ? "checking" : "checked")
            .Append(" page_changes=")
            .Append(SetupPage.ChangeCount(Page, side.Changes));
        _effect.Describe(text);
        text.Append("\nitem_picker=").Append(_picker.For);
        if (_picker.Open)
            text.Append("\nitem_rows=").Append(_picker.Rows);
    }
}
