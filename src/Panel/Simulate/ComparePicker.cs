using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// Picking side B from what each choice holds: side A with changes, a preset, a loadout shown with its gear, or a
/// saved run. Shown in place of the results until something is picked or it is closed.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class ComparePicker
{
    private const float RunRowHeight = 34;

    private readonly PanelContainer _root;
    private readonly VBoxContainer _presets;
    private readonly Label _presetsTitle;
    private readonly Label _presetSaid;
    private string? _renaming;
    private string? _deleting;
    private readonly HBoxContainer _loadouts;
    private readonly Label _loadoutsTitle;
    private readonly VirtualRows _runs;
    private readonly Label _runsTitle;
    private List<RunHeader> _headers = [];
    private int _presetCount;
    private int _loadoutCount;
    private readonly Label _recentTitle;
    private readonly VBoxContainer _recent;
    private int _recentCount;
    private string? _recentShows;

    public ComparePicker()
    {
        _root = Look.Panel(Art.SideBox, "Cs2ComparePicker");
        _root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _root.Visible = false;
        var column = Ui.Add(_root, Ui.Column(10));
        var head = Ui.Add(column, Ui.Row(8));
        var titles = Ui.Add(head, Ui.Column(2));
        titles.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titles.AddChild(Ui.Label(Tr.Get("Compare"), TextStyle.Heading));
        titles.AddChild(
            SideLine.Make(
                Tr.Format("Pick {0}. It runs on the same targets with the same seed as {1}.", SideLine.B, SideLine.A),
                TextStyle.Small
            )
        );
        head.AddChild(Ui.CloseButton("Cs2PickClose", () => Closed?.Invoke()));

        var changes = Ui.Add(column, Look.Panel(Art.RowBox, "Cs2PickChanges"));
        var changesRow = Ui.Add(changes, Ui.Row(12));
        var changesText = Ui.Add(changesRow, Ui.Column(2));
        changesText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        changesText.AddChild(SideLine.Make(Tr.Format("{0} with changes", SideLine.A), TextStyle.BodyStrong));
        changesText.AddChild(
            SideLine.Make(
                Tr.Format("Start from {0} and change gear, levels, food, prayers or a spell.", SideLine.A),
                TextStyle.Small
            )
        );
        var variant = Ui.Button(Tr.Get("Compare variant"), Look.Action8, "Cs2PickTry", () => TryPicked?.Invoke());
        variant.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        variant.TooltipText = Tr.Format(
            "Side {0} starts as a copy of side {1}, for you to change",
            SideLine.B,
            SideLine.A
        );
        // The green button's text at the size of the picker's other buttons: the large one shouts over the rows.
        void Size()
        {
            variant.AddThemeFontSizeOverride("font_size", variant.GetThemeFontSize("font_size", Look.Button4));
        }
        Look.OnTheme(variant, Size);
        Size();
        // Off the row box's edge, as its text is on the other side.
        changesRow.AddChild(Ui.Margin(variant, 0, 0, 8, 0));

        // Presets, and importing a line another player exported: a setup becomes a side, a preset is saved or used.
        var presetsHead = Ui.Add(column, Ui.Row(8));
        _presetsTitle = Ui.Add(presetsHead, Ui.Label("", TextStyle.Label));
        _presetsTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _presetSaid = Ui.Add(presetsHead, Ui.Label("", TextStyle.Small, "Cs2PickSaid"));
        var paste = Ui.Button(
            Tr.Get("Import…"),
            Look.Button4,
            "Cs2PickPaste",
            () => PasteDialog.Open(_root, () => Closed?.Invoke())
        );
        paste.TooltipText = Tr.Get("Import a setup or preset another player exported");
        presetsHead.AddChild(paste);
        _presets = Ui.Add(column, Ui.Column(4));

        _loadoutsTitle = Ui.Add(column, Ui.Label(Tr.Get("Your loadouts"), TextStyle.Label));
        _loadouts = Ui.Add(column, Ui.Row(10));

        _recentTitle = Ui.Add(column, Ui.Label(Tr.Get("Recent comparisons"), TextStyle.Label));
        _recent = Ui.Add(column, Ui.Column(4, "Cs2PickRecent"));

        var runsHead = Ui.Add(column, Ui.Row(8));
        _runsTitle = Ui.Label(Tr.Get("Saved runs"), TextStyle.Label);
        _runsTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        runsHead.AddChild(_runsTitle);
        var history = Ui.Button(
            Tr.Get("All in History ▸"),
            Look.Button4,
            "Cs2PickHistory",
            () => HistoryPressed?.Invoke()
        );
        history.Flat = true;
        runsHead.AddChild(history);
        _runs = new VirtualRows("Cs2PickRuns", RunRowHeight, MakeRunRow, BindRunRow);
        column.AddChild(_runs.Root);
    }

    public Control Root => _root;

    public event Action? Closed;

    public event Action? TryPicked;

    public event Action<string>? PresetPicked;

    public event Action<string>? LoadoutPicked;

    public event Action<string>? RunPicked;

    public event Action? HistoryPressed;

    /// <summary>What the picker lists, as <c>cs2 ui</c> prints it.</summary>
    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"picker_rows presets={_presetCount} loadouts={_loadoutCount} recent={_recentCount} runs={_headers.Count} built={_runs.Built}"
        );

    /// <summary>Lists the presets, loadouts and saved runs again.</summary>
    public void Show()
    {
        Overlays.Opened(this, () => PanelState.PickingBaseline, () => PanelState.PickBaseline(false));
        var game = LiveGame.Find();
        var names = PanelState.Names();
        var presets = PanelState.Presets().List;
        var loadouts = game != null ? LiveGame.Loadouts(game).Where(l => l.Unlocked).ToList() : [];
        var gears = loadouts.Select(l => LiveGame.Gear(game!, l.Slot)).ToList();
        ListRuns();
        ListRecent();
        // The rows and cards are made again only when what they show changed: making them every time the picker
        // opens cost that frame several times a frame's length.
        var shows = string.Join(
            '\n',
            [
                Tr.Version.ToString(CultureInfo.InvariantCulture),
                _renaming ?? "",
                _deleting ?? "",
                PanelState.A.Base.Text + " " + PanelState.A.Changes.ShortForm,
                .. presets.Select(p => p.Name + "=" + p.Changes),
                .. loadouts.Select(
                    (l, i) =>
                        string.Join(
                            '|',
                            [l.Slot, l.Name, gears[i].Style, gears[i].Stance, gears[i].Food ?? "", .. gears[i].Items]
                        )
                ),
            ]
        );
        if (shows == _shows)
            return;
        _shows = shows;

        Ui.Clear(_presets);
        _presetCount = presets.Count;
        _presetsTitle.Text =
            presets.Count > 0
                ? Tr.Get("Presets · shared by every character")
                : Tr.Get("Presets · none yet: save one from the Setup tab");
        for (var i = 0; i < presets.Count; i++)
            PresetRow(presets[i], i);

        Ui.Clear(_loadouts);
        _loadoutCount = loadouts.Count;
        _loadoutsTitle.Visible = loadouts.Count > 0;
        for (var i = 0; i < loadouts.Count; i++)
        {
            var loadout = loadouts[i];
            var n = i.ToString(CultureInfo.InvariantCulture);
            var gear = gears[i];
            var card = Ui.Add(_loadouts, Look.Panel(Art.RowBox, "Cs2PickLoadout" + n));
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var body = Ui.Add(card, Ui.Column(6));
            body.AddChild(Ui.Clipped(loadout.Name, TextStyle.BodyStrong));
            var icons = Ui.Add(body, new HFlowContainer());
            foreach (var item in gear.Items)
            {
                var icon = Ui.Picture(ChangeOptions.Icon(item), 28);
                icon.TooltipText = names(item);
                icon.MouseFilter = Control.MouseFilterEnum.Pass;
                icons.AddChild(icon);
            }
            if (gear.Items.Count == 0)
                body.AddChild(Ui.Label(Tr.Get("Nothing worn"), TextStyle.Small));
            body.AddChild(
                Ui.Clipped(
                    string.Join(
                        " · ",
                        new[]
                        {
                            gear.Style,
                            gear.Stance,
                            gear.Food is { } food ? names(food) : Tr.Get("No food"),
                        }.Where(p => !string.IsNullOrEmpty(p))
                    ),
                    TextStyle.Small
                )
            );
            var pick = Ui.Button(
                Tr.Get("Compare"),
                Look.Button8,
                "Cs2PickLoadout" + n + "Pick",
                () => LoadoutPicked?.Invoke(loadout.Slot)
            );
            pick.Disabled = PanelState.A.Base.Equals(SideBase.Loadout(loadout.Slot)) && PanelState.A.Changes.IsEmpty;
            pick.TooltipText = pick.Disabled
                ? Tr.Format("This is side {0}", SideLine.A)
                : Tr.Format("Makes this loadout side {0}", SideLine.B);
            body.AddChild(pick);
        }
    }

    private string? _shows;

    // The kept comparisons, made again only when they changed. Nothing shows when none is kept.
    private void ListRecent()
    {
        var shows = Tr.Version.ToString(CultureInfo.InvariantCulture) + RecentComparisons.Stamp();
        if (shows == _recentShows)
            return;
        _recentShows = shows;
        _recentCount = RecentComparisons.Fill(
            _recent,
            "Cs2PickRecent",
            () => Closed?.Invoke(),
            null,
            reason => _presetSaid.Text = reason
        );
        _recentTitle.Visible = _recent.Visible = _recentCount > 0;
    }

    private void ListRuns()
    {
        _headers = [.. RunStore.Shared.List().Where(h => h.State is RunState.Finished or RunState.Cancelled)];
        _runsTitle.Text =
            _headers.Count == 0
                ? Tr.Get("Saved runs · none yet")
                : Tr.Plural("Saved runs · {0}", "Saved runs · {0}", _headers.Count, _headers.Count);
        _runs.SetCount(_headers.Count);
    }

    // A preset: its name and what it holds, then Use, Rename, Delete (asking once) and Export.
    private void PresetRow(Preset preset, int index)
    {
        var n = "Cs2PickPreset" + index.ToString(CultureInfo.InvariantCulture);
        var row = Ui.Add(Ui.Add(_presets, Look.Panel(Art.RowBox, n)), Ui.Row(10));
        if (_renaming == preset.Name)
        {
            var field = new LineEdit
            {
                Name = n + "Name",
                Text = preset.Name,
                MaxLength = 60,
            };
            field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            Look.DarkField(field);
            void Done()
            {
                var result = PanelState.Presets().Rename(preset.Name, field.Text);
                if (result == PresetResult.Saved)
                    PanelState.SavePresets();
                _renaming = null;
                _presetSaid.Text = result switch
                {
                    PresetResult.Saved => "",
                    PresetResult.NameTaken => Tr.Get("Another preset has that name."),
                    _ => Tr.Get("Give the preset a name."),
                };
                Show();
            }
            field.TextSubmitted += _ => Done();
            row.AddChild(field);
            row.AddChild(Ui.Button(Tr.Get("Save"), Look.Button4, n + "RenameSave", Done));
            row.AddChild(
                Ui.Button(Tr.Get("Cancel"), Look.Button4, n + "RenameCancel", () => Again(() => _renaming = null))
            );
            Callable.From(field.GrabFocus).CallDeferred();
            return;
        }
        var name = Ui.Label(preset.Name, TextStyle.Label);
        // The name is the player's own text, or a sender's: shown as written, never looked up as a translation.
        name.AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled;
        row.AddChild(name);
        var readable = preset.Read(out var problem) != null;
        var detail = Ui.Clipped("", TextStyle.Small);
        detail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SideLine.OnLabel(
            detail,
            readable
                ? Tr.Plural(
                    "{0} change · on top of {1}",
                    "{0} changes · on top of {1}",
                    preset.Count,
                    preset.Count,
                    SideLine.A
                )
                : Tr.Format("Can't be used: {0}", problem ?? ""),
            TextStyle.Small
        );
        row.AddChild(detail);
        var use = Ui.Button(Tr.Get("Use"), Look.Button4, n + "Use", () => PresetPicked?.Invoke(preset.Name));
        use.Disabled = !readable;
        use.TooltipText = Tr.Format(
            "Side {0}: what side {1} starts from, with this preset's changes",
            SideLine.B,
            SideLine.A
        );
        row.AddChild(use);
        row.AddChild(
            Ui.Button(Tr.Get("Rename"), Look.Button4, n + "Rename", () => Again(() => _renaming = preset.Name))
        );
        var deleting = _deleting == preset.Name;
        row.AddChild(
            Ui.Button(
                deleting ? Tr.Get("Delete?") : Tr.Get("Delete"),
                Look.Cancel8,
                n + "Delete",
                () =>
                    Again(() =>
                    {
                        if (!deleting)
                        {
                            _deleting = preset.Name;
                            return;
                        }
                        if (PanelState.Presets().Delete(preset.Name))
                            PanelState.SavePresets();
                        _deleting = null;
                    })
            )
        );
        var copy = Ui.Button(Tr.Get("Export"), Look.Button4, n + "Copy");
        copy.Pressed += () => FloatNote.Show(copy, Sharing.ExportPreset(preset) ?? Sharing.Exported);
        copy.Disabled = !readable;
        copy.TooltipText = Tr.Get("Copies this preset as one line, for another player to import");
        row.AddChild(copy);
    }

    // Changes what the rows show, and lists them again.
    private void Again(Action change)
    {
        change();
        _presetSaid.Text = "";
        Show();
    }

    private static PanelContainer MakeRunRow()
    {
        var row = Look.Panel(Art.RowBox);
        var line = Ui.Add(row, Ui.Row(12));
        line.AddChild(Ui.Clipped("", TextStyle.Label));
        line.AddChild(Ui.Clipped("", TextStyle.Small));
        var pick = Ui.Button(Tr.Get("Compare"), Look.Button4, "Pick");
        pick.TooltipText = Tr.Format("Compares side {0} against this saved run's results", SideLine.A);
        line.AddChild(pick);
        return row;
    }

    private void BindRunRow(Control row, int index)
    {
        var header = _headers[index];
        row.Name = "Cs2PickRun" + index.ToString(CultureInfo.InvariantCulture);
        var line = row.GetChild(0);
        Ui.SetClipped(line.GetChild<Label>(0), header.Name ?? header.Id);
        var setup = header.Setup == Setup.Current ? Tr.Get("Character as is") : header.Setup;
        var changes = header.Changes is { Length: > 0 } text
            ? text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
            : 0;
        var parts = new List<string> { setup };
        if (changes > 0)
            parts.Add(Tr.Plural("{0} change", "{0} changes", changes, changes));
        parts.Add(Tr.Plural("{0} target", "{0} targets", header.Targets, header.Targets));
        parts.Add((header.EndedAt ?? header.StartedAt).ToLocalTime().ToString("g", Tr.Culture));
        if (header.State == RunState.Cancelled)
            parts.Add(Tr.Get("cancelled"));
        Ui.SetClipped(line.GetChild<Label>(1), string.Join(" · ", parts));
        var pick = line.GetChild<Button>(2);
        // A reused row gets the pick of the run it shows now.
        foreach (var connection in pick.GetSignalConnectionList(BaseButton.SignalName.Pressed))
            pick.Disconnect(BaseButton.SignalName.Pressed, connection["callable"].AsCallable());
        pick.Pressed += () => RunPicked?.Invoke(header.Id);
    }
}
