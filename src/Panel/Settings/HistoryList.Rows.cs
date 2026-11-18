using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

// A saved run's row: its columns, and its actions as they change (show, compare, pin, rename, delete, confirm).
internal sealed partial class HistoryList
{
    private PanelContainer Row(RunHeader header)
    {
        var index = _shown++;
        var selected = header.Id == _selected;
        var root = Look.Panel(
            Art.RowBox,
            Ui.Id("Cs2HistoryRow", index.ToString(System.Globalization.CultureInfo.InvariantCulture))
        );
        if (selected)
            PixelBox.Panel(
                root,
                new StyleBoxFlat
                {
                    BgColor = Colors.White with { A = 0.06f },
                    BorderColor = Look.Best,
                    BorderWidthBottom = 1,
                    BorderWidthTop = 1,
                    BorderWidthLeft = 1,
                    BorderWidthRight = 1,
                    ContentMarginLeft = 10,
                    ContentMarginRight = 10,
                    ContentMarginTop = 6,
                    ContentMarginBottom = 6,
                }
            );
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        var column = Ui.Add(root, Ui.Column(6));
        var line = Ui.Add(column, Ui.Row(12));

        var pin = Ui.Button("", "", "Cs2HistoryPin", () => Pin(header));
        pin.Flat = true;
        pin.Icon = Art.File(Art.PinPath);
        pin.ExpandIcon = true;
        pin.CustomMinimumSize = new Vector2(28, 28);
        pin.Modulate = header.Pinned ? Look.Best : Colors.White with { A = 0.45f };
        pin.TooltipText = header.Pinned ? Tr.Get("Unpin") : Tr.Get("Pin: keep past the limit");
        line.AddChild(pin);

        var who = Ui.Add(line, Ui.Column(0));
        who.CustomMinimumSize = new Vector2(NameWidth, 0);
        var setup = SetupText(header);
        var renaming = selected && header.Id == _renaming;
        if (renaming)
        {
            var edit = new LineEdit
            {
                Name = "Cs2HistoryRename",
                Text = header.Name ?? "",
                MaxLength = RunHistory.MaxNameLength,
                PlaceholderText = setup,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            Look.DarkField(edit);
            edit.TextSubmitted += text => Rename(header, text);
            edit.GuiInput += e =>
            {
                if (e is InputEventKey { Keycode: Key.Escape, Pressed: true })
                {
                    _renaming = null;
                    edit.AcceptEvent();
                    Rebuild();
                }
            };
            who.AddChild(edit);
            edit.CallDeferred(Control.MethodName.GrabFocus);
        }
        else
            who.AddChild(Ui.Clipped(header.Name ?? setup, TextStyle.BodyStrong));
        var seed = Tr.Format("seed {0}", header.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var under = Ui.Add(who, Ui.Row(6));
        under.AddChild(Ui.Clipped(header.Name != null ? Tr.Format("{0} · {1}", setup, seed) : seed, TextStyle.Tiny));
        if (RunMarks.AnotherBuild(header.GameBuild) is { } another)
            under.AddChild(another);
        if (header.Checked == false)
            under.AddChild(RunMarks.Unchecked("Cs2HistoryUnchecked"));

        var changes =
            header.Changes is { Length: > 0 } c ? c
            : header.IgnoreRequirements ? Tr.Get("Ignore requirements")
            : Tr.Get("No changes");
        Column(line, changes, 160, expand: true);
        Column(line, Tr.Plural("{0} target", "{0} targets", header.Targets, header.Targets), 90);
        Column(line, (header.EndedAt ?? header.StartedAt).ToLocalTime().ToString("g", Tr.Culture), 150);
        Column(line, StateText(header.State), 80);

        // The selected row's actions, on a line of their own under it.
        if (selected)
        {
            var actions = Ui.Add(column, Ui.Row(8, "Cs2HistoryActions"));
            actions.Alignment = BoxContainer.AlignmentMode.End;
            if (header.Id == _confirming)
                Confirm(actions, header);
            else if (renaming)
                RenameActions(actions, header);
            else
                Actions(actions, header, index);
        }

        root.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && _selected != header.Id)
            {
                _selected = header.Id;
                _renaming = null;
                _confirming = null;
                Say(null);
                Rebuild();
            }
        };
        return root;
    }

    private void Actions(HBoxContainer actions, RunHeader header, int index)
    {
        var busy = RunService.Instance.IsActive;
        var show = Ui.Button(Tr.Get("Show"), Look.Button4, "Cs2HistoryShow", () => Show(header));
        show.Disabled = busy;
        show.TooltipText = busy ? Tr.Get("A run is in progress") : Tr.Get("Show this run's results");
        actions.AddChild(show);
        var compare = Ui.Button(
            Tr.Get("Compare now with this"),
            Look.Button4,
            Ui.Id("Cs2HistoryCompare", index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            () => CompareChosen?.Invoke(header.Id)
        );
        compare.Disabled = busy;
        compare.TooltipText = busy
            ? Tr.Get("A run is in progress")
            : Tr.Format("Simulate: your setup now as {0}, this run as {1}", SideLine.A, SideLine.B);
        actions.AddChild(compare);
        actions.AddChild(
            Ui.Button(
                Tr.Get("Rename"),
                Look.Button4,
                "Cs2HistoryRenameButton",
                () =>
                {
                    _renaming = header.Id;
                    Rebuild();
                }
            )
        );
        var delete = Ui.Button(
            Tr.Get("Delete"),
            Look.Cancel8,
            "Cs2HistoryDelete",
            () =>
            {
                _confirming = header.Id;
                Overlays.Opened(
                    this,
                    () => _confirming != null && _root.IsVisibleInTree(),
                    () =>
                    {
                        _confirming = null;
                        Rebuild();
                    }
                );
                Rebuild();
            }
        );
        if (RunService.Instance.DeleteRefusal(header) is { } refusal)
        {
            delete.Disabled = true;
            var why = Capitalised(Tr.Note(refusal));
            delete.TooltipText = why;
            // Said beside it too: a disabled button's tooltip is easy to miss.
            var reason = Ui.Label(why, TextStyle.Tiny);
            reason.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            actions.AddChild(reason);
        }
        actions.AddChild(delete);
    }

    private void Confirm(HBoxContainer actions, RunHeader header)
    {
        actions.AddChild(Ui.Label(Tr.Get("Delete this run?"), TextStyle.BodyStrong));
        actions.AddChild(Ui.Button(Tr.Get("Delete"), Look.Cancel8, "Cs2HistoryConfirm", () => Delete(header)));
        actions.AddChild(
            Ui.Button(
                Tr.Get("Cancel"),
                Look.Button4,
                "Cs2HistoryCancel",
                () =>
                {
                    _confirming = null;
                    Rebuild();
                }
            )
        );
    }

    private void RenameActions(HBoxContainer actions, RunHeader header)
    {
        actions.AddChild(Ui.Label(Tr.Get("Enter saves · Esc cancels · empty clears the name"), TextStyle.Tiny));
        actions.AddChild(
            Ui.Button(Tr.Get("Save"), Look.Action8, "Cs2HistorySave", () => Rename(header, CurrentEdit()))
        );
        actions.AddChild(
            Ui.Button(
                Tr.Get("Cancel"),
                Look.Button4,
                "Cs2HistoryCancel",
                () =>
                {
                    _renaming = null;
                    Rebuild();
                }
            )
        );
    }

    private PanelContainer Unreadable(UnreadableRun file)
    {
        var root = Look.Panel(Art.RowBox, Ui.Id("Cs2HistoryUnreadable", file.File));
        var line = Ui.Add(root, Ui.Row(12));
        var text = Ui.Add(line, Ui.Column(0));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(Ui.Clipped(file.File, TextStyle.BodyStrong));
        var why = Ui.Label(Tr.Format("Unreadable: {0}", file.Why), TextStyle.Tiny, wrap: true);
        why.Modulate = Look.Deadly;
        text.AddChild(why);
        line.AddChild(
            Ui.Button(
                Tr.Get("Delete"),
                Look.Cancel8,
                "Cs2HistoryDelete",
                () =>
                {
                    RunService.Instance.DeleteRun(file.File);
                    Rebuild();
                }
            )
        );
        return root;
    }

    // A column of the row: clipped at its width so a long value never widens the list past Settings.
    private static void Column(HBoxContainer line, string text, float width, bool expand = false)
    {
        var label = Ui.Clipped(text, TextStyle.Small);
        label.CustomMinimumSize = new Vector2(width, 0);
        label.SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill;
        line.AddChild(label);
    }

    private static string SetupText(RunHeader header)
    {
        var setup = header.Setup == Setup.Current ? Tr.Get("Character as is") : header.Setup;
        return header.IgnoreRequirements ? Tr.Format("{0} · requirements ignored", setup) : setup;
    }

    private static string StateText(RunState state) =>
        state switch
        {
            RunState.Finished => Tr.Get("Finished"),
            RunState.Cancelled => Tr.Get("Cancelled"),
            RunState.Error => Tr.Get("Failed"),
            _ => Tr.Get("Running"),
        };

    private static string Capitalised(string text) =>
        text.Length > 0 ? char.ToUpper(text[0], Tr.Culture) + text[1..] : text;
}
