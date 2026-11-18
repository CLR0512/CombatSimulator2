using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// What a side starts from, picked from what each choice holds: the character as it is now, each loadout with its
/// gear, the presets, for side B the most recent saved runs, and an imported setup. Picking closes it.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class BasePicker
{
    private const int RunsShown = 5;

    // A section shows this many rows and scrolls the rest, so a long list of presets keeps the picker on screen.
    private const int RowsShown = 3;
    private const int RowGap = 6;

    private readonly PanelContainer _root;
    private readonly SideLine _title = new(TextStyle.BodyStrong);
    private readonly VBoxContainer _rows;
    private VBoxContainer _into = null!;
    private SideLetter _letter;

    public BasePicker()
    {
        // The dark panel the Areas list and the dropdowns open in: opaque, where the popup frame is see-through.
        _root = Look.Panel(Art.TooltipDarkBox, "Cs2BasePicker");
        _root.Visible = false;
        _root.CustomMinimumSize = new Vector2(520, 0);
        var column = Ui.Add(_root, Ui.Column(8));
        var head = Ui.Add(column, Ui.Row(8));
        _title.Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(_title.Root);
        head.AddChild(Ui.CloseButton("Cs2BaseClose", Close));
        _rows = Ui.Add(column, Ui.Column(RowGap));
    }

    public Control Root => _root;

    public bool Open => _root.Visible;

    /// <summary>The side the picker is open for.</summary>
    public SideLetter Letter => _letter;

    /// <summary>Raised with the side and the base picked for it.</summary>
    public event Action<SideLetter, SideBase>? Picked;

    /// <summary>Raised with the side and the name of the preset whose changes it takes.</summary>
    public event Action<SideLetter, string>? PresetPicked;

    public void Close() => _root.Visible = false;

    /// <summary>A side's Change: closes the picker open for that side, else shows it for that side.</summary>
    public void Toggle(SideLetter letter, SideState? side)
    {
        if (Open && _letter == letter)
            Close();
        else
            Show(letter, side);
    }

    /// <summary>Lists the choices for <paramref name="letter"/>'s side, marking the one in use.</summary>
    public void Show(SideLetter letter, SideState? side)
    {
        _letter = letter;
        _changes = side?.Changes.Count ?? 0;
        Overlays.Opened(this, _root.IsVisibleInTree, Close);
        _title.Show(Tr.Format("{0} starts from", letter == SideLetter.A ? SideLine.A : SideLine.B));
        Ui.Clear(_rows);
        if (LiveGame.Find() is not { } game)
            return;
        var character = LiveGame.CharacterName(game);
        var index = 0;
        _into = _rows;
        Row(
            ref index,
            Tr.Format("{0} as you are now", character),
            LiveGame.Gear(game, null),
            SideBase.Current,
            side?.Base
        );
        var loadouts = LiveGame.Loadouts(game).Where(l => l.Unlocked).ToList();
        if (loadouts.Count > 0)
        {
            var section = Section(Tr.Get("Your loadouts"));
            foreach (var loadout in loadouts)
                Row(
                    ref index,
                    loadout.Name,
                    LiveGame.Gear(game, loadout.Slot),
                    SideBase.Loadout(loadout.Slot),
                    side?.Base
                );
            Cap(section);
        }
        var presets = PanelState.Presets().List;
        if (presets.Count > 0)
        {
            var section = Section(Tr.Get("Presets · shared by every character"));
            foreach (var preset in presets)
                PresetRow(ref index, preset, side is { CanChange: true } ? side.Changes.ShortForm : null);
            Cap(section);
        }
        if (letter == SideLetter.B)
        {
            var runs = RunStore
                .Shared.List()
                .Where(h => h.State is RunState.Finished or RunState.Cancelled)
                .Take(RunsShown)
                .ToList();
            if (runs.Count > 0)
            {
                var section = Section(Tr.Get("Saved runs · results only"));
                foreach (var run in runs)
                    RunRow(ref index, run, side?.Base);
                Cap(section);
            }
        }
        else
            _rows.AddChild(SideLine.Make(Tr.Format("A saved run can only be side {0}.", SideLine.B), TextStyle.Tiny));
        // Or a setup another player exported: the dialog shows what it holds, then makes it either side.
        var paste = Ui.Button(
            Tr.Get("Import setup…"),
            Look.Button4,
            "Cs2BasePaste",
            () => PasteDialog.Open(_root, Close)
        );
        paste.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        paste.TooltipText = Tr.Get(
            "Reads a setup another player exported from the clipboard, and shows it before anything changes"
        );
        _rows.AddChild(paste);
        _root.Visible = true;
    }

    private void Row(ref int index, string name, SetupGear gear, SideBase choice, SideBase? inUse)
    {
        var line = NewRow(ref index, out var number);
        line.AddChild(Ui.Label(name, TextStyle.Label));
        var icons = Ui.Add(line, Ui.Row(1));
        foreach (var item in gear.Items.Take(8))
            icons.AddChild(Ui.Picture(ChangeOptions.Icon(item), 22));
        var names = PanelState.Names();
        var words = Ui.Clipped(
            string.Join(
                " · ",
                new[] { gear.Style, gear.Stance, gear.Food is { } food ? names(food) : null }.Where(p =>
                    !string.IsNullOrEmpty(p)
                )
            ),
            TextStyle.Small
        );
        line.AddChild(words);
        Pick(line, number, choice, inUse);
    }

    private void RunRow(ref int index, RunHeader run, SideBase? inUse)
    {
        var line = NewRow(ref index, out var number);
        line.AddChild(Ui.Label(run.Name ?? run.Id, TextStyle.Label));
        var setup = run.Setup == Setup.Current ? Tr.Get("as it was") : run.Setup;
        var when = (run.EndedAt ?? run.StartedAt).ToLocalTime().ToString("g", Tr.Culture);
        line.AddChild(
            Ui.Clipped(
                string.Join(" · ", setup, Tr.Plural("{0} target", "{0} targets", run.Targets, run.Targets), when),
                TextStyle.Small
            )
        );
        Pick(line, number, SideBase.SavedRun(run.Id), inUse);
    }

    // A preset keeps the side's start and puts its changes in place of the side's own.
    private void PresetRow(ref int index, Preset preset, string? changesInUse)
    {
        var line = NewRow(ref index, out var number);
        var name = Ui.Label(preset.Name, TextStyle.Label);
        // The name is the player's own text: shown as written, never looked up as a translation.
        name.AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled;
        line.AddChild(name);
        var readable = preset.Read(out var problem) != null;
        var detail = Ui.Clipped(
            readable
                ? Tr.Plural("{0} change", "{0} changes", preset.Count, preset.Count)
                : Tr.Format("Can't be used: {0}", problem ?? ""),
            TextStyle.Small
        );
        detail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(detail);
        if (preset.Changes == changesInUse)
        {
            line.AddChild(Ui.Label(Tr.Get("in use"), TextStyle.Tiny));
            return;
        }
        var pick = Ui.Button(
            Tr.Get("Pick"),
            Look.Button4,
            "Cs2BasePick" + number,
            () =>
            {
                Close();
                PresetPicked?.Invoke(_letter, preset.Name);
            }
        );
        pick.Disabled = !readable;
        var side = _letter == SideLetter.A ? SideLine.A : SideLine.B;
        pick.TooltipText =
            _changes == 0
                ? Tr.Format("Side {0} keeps its start and takes this preset's changes", side)
                : Tr.Plural(
                    "Side {0} keeps its start and takes this preset's changes. Replaces its {1} change.",
                    "Side {0} keeps its start and takes this preset's changes. Replaces its {1} changes.",
                    _changes,
                    side,
                    _changes.ToString(Tr.Culture)
                );
        line.AddChild(pick);
    }

    // A titled list whose rows the next rows go into, in a box that scrolls once it holds more than it shows.
    private ScrollContainer Section(string title)
    {
        _rows.AddChild(Ui.Label(title, TextStyle.Small));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        // Room between the rows and the bar, so it never sits on a Pick button.
        scroll.AddThemeConstantOverride("scrollbar_h_separation", 6);
        Thin(scroll.GetVScrollBar());
        // The rows fade out at an end with more past it, as the Areas list's do.
        _rows.AddChild(ScrollFade.Wrap(scroll));
        _into = Ui.Add(scroll, Ui.Column(RowGap));
        _into.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return scroll;
    }

    // A slim bar in the picker's own neutral tone: the theme's broad brown one crowds the rows' Pick buttons.
    private static void Thin(ScrollBar bar)
    {
        var tone = Color.FromHtml("#b9b4a8");
        StyleBoxFlat Box(Color colour)
        {
            var box = new StyleBoxFlat { BgColor = colour };
            box.ContentMarginLeft = box.ContentMarginRight = 2;
            box.SetCornerRadiusAll(2);
            return box;
        }
        bar.AddThemeStyleboxOverride("scroll", Box(Colors.White with { A = 0.05f }));
        bar.AddThemeStyleboxOverride("scroll_focus", Box(Colors.White with { A = 0.05f }));
        bar.AddThemeStyleboxOverride("grabber", Box(tone with { A = 0.45f }));
        bar.AddThemeStyleboxOverride("grabber_highlight", Box(tone with { A = 0.7f }));
        bar.AddThemeStyleboxOverride("grabber_pressed", Box(tone with { A = 0.85f }));
    }

    // The section's box as tall as its first rows; the rows after them scroll. Later rows go back in the picker.
    private void Cap(ScrollContainer section)
    {
        var shown = _into.GetChildren().OfType<Control>().Take(RowsShown).ToList();
        var height = shown.Sum(c => c.GetCombinedMinimumSize().Y) + RowGap * Math.Max(0, shown.Count - 1);
        section.CustomMinimumSize = new Vector2(0, height);
        if (_into.GetChildCount() <= RowsShown)
            section.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _into = _rows;
    }

    private HBoxContainer NewRow(ref int index, out string number)
    {
        number = (index++).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var row = Ui.Add(_into, Look.Panel(Art.RowBox, "Cs2BaseRow" + number));
        return Ui.Add(row, Ui.Row(8));
    }

    private void Pick(HBoxContainer line, string number, SideBase choice, SideBase? inUse)
    {
        if (choice.Equals(inUse))
        {
            line.AddChild(Ui.Label(Tr.Get("in use"), TextStyle.Tiny));
            return;
        }
        var pick = Ui.Button(
            Tr.Get("Pick"),
            Look.Button4,
            "Cs2BasePick" + number,
            () =>
            {
                Close();
                Picked?.Invoke(_letter, choice);
            }
        );
        // What a pick does to the side, said before it is pressed: its changes go with the old start.
        var side = _letter == SideLetter.A ? SideLine.A : SideLine.B;
        pick.TooltipText =
            choice.Kind == SideBaseKind.SavedRun ? Tr.Format("Compares side {0} against this saved run", SideLine.A)
            : _changes == 0 ? Tr.Format("Side {0} starts from this", side)
            : Tr.Plural(
                "Side {0} starts from this. Clears its {1} change.",
                "Side {0} starts from this. Clears its {1} changes.",
                _changes,
                side,
                _changes.ToString(Tr.Culture)
            );
        line.AddChild(pick);
    }

    private int _changes;
}
