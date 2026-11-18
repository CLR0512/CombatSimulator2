using System.Globalization;
using System.Text;
using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// What a line on the clipboard holds, before anything changes: each change checked on this character, applying or
/// not with the reason, then Use as A, Use as B, or for a preset Save as preset. A line CS2 can't read shows why and
/// nothing else. The line is untrusted text: it is only ever read into a change list, and a preset's name is shown
/// as plain text.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class PasteDialog
{
    private const float MaxRowsHeight = 440;

    private static PasteDialog? _open;

    private readonly Control _catcher;
    private readonly PanelContainer _panel;
    private readonly VBoxContainer _column;
    private readonly VBoxContainer _rows;
    private readonly Label _said;
    private readonly HBoxContainer _naming;
    private readonly LineEdit _name;
    private readonly Label _title;
    private readonly Action? _used;
    private ChangeList _changes = ChangeList.Empty;
    private string _described = "";
    private string _kind = "setup";
    private bool _replacing;

    private PasteDialog(Control host, Action? used)
    {
        _used = used;
        Overlays.Opened(this, () => _open == this, Close);
        // Over the window, under the dialog: a click outside it closes it and goes no further.
        _catcher = new Control
        {
            Name = "Cs2PasteOutside",
            TopLevel = true,
            ZIndex = 19,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _catcher.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true })
            {
                _catcher.AcceptEvent();
                Close();
            }
        };
        host.AddChild(_catcher);
        _panel = Look.Panel(Art.TooltipDarkBox, "Cs2PasteDialog");
        _panel.TopLevel = true;
        _panel.ZIndex = 20;
        _panel.CustomMinimumSize = new Vector2(620, 0);
        var column = _column = Ui.Add(_panel, Ui.Column(10));
        var head = Ui.Add(column, Ui.Row(8));
        _title = Ui.Add(head, Ui.Label(Tr.Get("Import setup"), TextStyle.Heading));
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(Ui.CloseButton("Cs2PasteCancel", Close));
        // A whole setup lists dozens of entries: they scroll past a height that leaves the window around it.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(ScrollFade.Wrap(scroll));
        _rows = Ui.Add(scroll, Ui.Column(4));
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _rows.MinimumSizeChanged += () =>
            scroll.CustomMinimumSize = new Vector2(0, Math.Min(_rows.GetCombinedMinimumSize().Y, MaxRowsHeight));
        _said = Ui.Add(column, Ui.Label("", TextStyle.Small, "Cs2PasteSaid", wrap: true));
        _naming = Ui.Add(column, Ui.Row(8));
        _name = new LineEdit
        {
            Name = "Cs2PasteName",
            PlaceholderText = Tr.Get("Preset name"),
            CustomMinimumSize = new Vector2(260, 0),
            MaxLength = 60,
        };
        Look.DarkField(_name);
        _naming.AddChild(_name);
        _name.TextChanged += _ => _replacing = false;
        host.AddChild(_panel);
        var scale = host.GetGlobalTransform().Scale;
        var room = host.GetGlobalRect();
        _catcher.GlobalPosition = room.Position;
        _catcher.Size = room.Size;
        _panel.Scale = scale;
        _panel.GlobalPosition = room.Position + new Vector2(room.Size.X * 0.3f, room.Size.Y * 0.18f);
    }

    /// <summary>Whether a dialog is open.</summary>
    public static bool IsOpen => _open != null;

    /// <summary>
    /// Reads the clipboard and shows what it holds, over the window <paramref name="from"/> is in;
    /// <paramref name="used"/> runs once the line is used as a side or saved as a preset.
    /// </summary>
    public static void Open(Control from, Action? used)
    {
        _open?.Close();
        if (Host(from) is not { } host)
            return;
        _open = new PasteDialog(host, used);
        _open.Read(Clipboard.Get());
    }

    /// <summary>The open dialog as <c>cs2 ui</c> prints it.</summary>
    public static void Describe(StringBuilder text)
    {
        if (_open != null)
            text.Append("\npaste ").Append(_open._described);
    }

    public void Close()
    {
        if (_open == this)
            _open = null;
        _catcher.QueueFree();
        _panel.QueueFree();
    }

    // CS2's window, which the dialog is drawn over.
    private static Control? Host(Control from)
    {
        Control? top = null;
        for (Node? node = from; node != null; node = node.GetParent())
            if (node is Control control)
            {
                top = control;
                if (control.Name == "Cs2Root")
                    break;
            }
        return top;
    }

    private void Read(string text)
    {
        if (LiveGame.Find() is not { } game)
        {
            Refuse(Tr.Note(LiveGame.NoGame), "no-game");
            return;
        }
        if (!SetupLine.TryRead(text, out var content, out var problem))
        {
            Refuse(Sharing.Problem(problem), problem.ToString().ToLowerInvariant());
            return;
        }
        if (content!.Kind == LineKind.Simulation)
        {
            ReadSimulation(content);
            return;
        }
        if (!LineIds.TryResolve(game, content, out var changes, out var missing))
        {
            Refuse(Sharing.Problem(LineProblem.Malformed), "malformed");
            return;
        }
        _changes = changes;
        var preset = content.Kind == LineKind.Preset;
        _kind = preset ? "preset" : "setup";
        // A preset's name is the sender's text: a label shows it as it is, never as markup.
        var what = Ui.Label(
            preset
                ? Tr.Format("Preset: {0} · {1}", content.Name ?? "", ChangeCount(changes.Count))
                : Tr.Format("A setup · {0}", ChangeCount(changes.Count)),
            TextStyle.BodyStrong
        );
        what.AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled;
        _rows.AddChild(what);
        if (content.GameBuild != LineIds.Build)
            _rows.AddChild(Ui.Label(Tr.Get("Made on another game build."), TextStyle.Small));
        foreach (var note in missing)
            _rows.AddChild(Row(Tr.Note(note), Tr.Get("not in this build"), Look.Risky));
        _name.Text = content.Name ?? "";
        _naming.Visible = preset;
        if (preset)
            _naming.AddChild(Ui.Button(Tr.Get("Save as preset"), Look.Button4, "Cs2PasteSave", Save));
        var actions = Ui.Add(_column, Ui.Row(8));
        actions.AddChild(Ui.Spacer());
        var asA = Ui.Button(Tr.Get("Use as A"), Look.Button8, "Cs2PasteAsA", () => Use(SideLetter.A));
        asA.TooltipText = Tr.Format("Replaces side {0}'s changes with these", SideLine.A);
        actions.AddChild(asA);
        var asB = Ui.Button(Tr.Get("Use as B"), Look.Action8, "Cs2PasteAsB", () => Use(SideLetter.B));
        asB.TooltipText = Tr.Format(
            "Side {0}: what side {1} starts from, with these changes. Replaces a side {0} already there.",
            SideLine.B,
            SideLine.A
        );
        actions.AddChild(asB);
        Describe("checking", changes.Count, missing.Count);
        if (changes.IsEmpty)
        {
            Describe("checked", 0, missing.Count);
            return;
        }
        // Checked on what side A starts from, as using it would: what applies here, and why not.
        var onto = new SideState(PanelState.A.Base, changes, PanelState.A.IgnoreRequirements);
        var checking = Ui.Add(_rows, Ui.Label(Tr.Get("Checking on your character…"), TextStyle.Small));
        RunService.CheckSide(
            onto.ToSetup(""),
            changes,
            (check, error) =>
            {
                if (_open != this)
                    return;
                checking.QueueFree();
                if (check == null)
                {
                    _said.Text = error?.Message ?? Tr.Get("The check failed.");
                    return;
                }
                var (applies, refused) = Outcomes(check, _rows.GetChildCount());
                Describe("checked", applies, missing.Count + refused);
            }
        );
    }

    // A check's outcomes as rows at <paramref name="at"/>: what applies and what is refused with why; what is already
    // as the line has it says nothing, counted on one line.
    private (int Applies, int Refused) Outcomes(SideCheck check, int at)
    {
        var names = PanelState.Names();
        var applies = 0;
        var same = 0;
        var refused = 0;
        var rows = new List<Control>();
        foreach (var outcome in check.Changes.Outcomes)
        {
            if (outcome.State == ChangeState.NoChange)
            {
                same++;
                continue;
            }
            var described = Tr.Note(ChangeNotes.Describe(outcome.Change, outcome.From, names));
            if (outcome.Applies)
                applies++;
            else
                refused++;
            rows.Add(
                outcome.Applies
                    ? Row(described, State(outcome.State), Look.Safe)
                    : Row(described, Tr.Notes(outcome.Reasons, "; "), Look.Deadly)
            );
        }
        if (same > 0)
            rows.Add(Ui.Label(Tr.Plural("{0} already as yours", "{0} already as yours", same, same), TextStyle.Small));
        foreach (var row in rows)
        {
            _rows.AddChild(row);
            _rows.MoveChild(row, Math.Min(at++, _rows.GetChildCount() - 1));
        }
        return (applies, refused);
    }

    private static string ChangeCount(int count) => Tr.Plural("{0} change", "{0} changes", count, count);

    private static string State(ChangeState state) =>
        state switch
        {
            ChangeState.RequirementIgnored => Tr.Get("requirement ignored"),
            _ => Tr.Get("applies"),
        };

    private HBoxContainer Row(string change, string state, Color colour)
    {
        var n = _rows.GetChildCount().ToString(CultureInfo.InvariantCulture);
        var row = Ui.Row(10, "Cs2PasteRow" + n);
        var text = Ui.Add(row, Ui.Label(change, TextStyle.Small));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var mark = Ui.Add(row, Ui.Label(state, TextStyle.Tiny));
        mark.Modulate = colour;
        return row;
    }

    private void Refuse(string why, string key)
    {
        _rows.AddChild(Ui.Label(why, TextStyle.Body, "Cs2PasteProblem", wrap: true));
        _naming.Visible = false;
        _described = "problem=" + key;
    }

    private void Describe(string state, int applies, int not) =>
        _described = string.Create(
            CultureInfo.InvariantCulture,
            $"kind={_kind} state={state} applies={applies} not={not}"
        );

    // The line's changes on what side A starts from; what the game refuses drops out as it does for any change.
    private void Use(SideLetter letter)
    {
        PanelState.SetSide(letter, new SideState(PanelState.A.Base, _changes, PanelState.A.IgnoreRequirements));
        Close();
        _used?.Invoke();
    }

    private void Save()
    {
        var presets = PanelState.Presets();
        switch (presets.Save(_name.Text, _changes, _replacing))
        {
            case PresetResult.Saved or PresetResult.Replaced:
                PanelState.SavePresets();
                Close();
                _used?.Invoke();
                return;
            case PresetResult.NameTaken:
                _replacing = true;
                _said.Text = Tr.Get("A preset has this name. Save again to replace it.");
                return;
            case PresetResult.NoName:
                _said.Text = Tr.Get("Give the preset a name.");
                return;
            case PresetResult.Full:
                _said.Text = Tr.Format("{0} presets already: delete one first.", Presets.Max.ToString(Tr.Culture));
                return;
            default:
                _said.Text = Tr.Get("No changes to save.");
                return;
        }
    }
}
