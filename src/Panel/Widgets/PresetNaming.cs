using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// Saving a side's changes as a preset: a name field whose ▾ lists the presets to update one of, Save and a ✕,
/// shown while a name is typed. A preset picked from the list is updated at once (Save reads Update); a name typed that is in use
/// asks first (Save then reads Replace). What came of it is said through the owner's own line.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class PresetNaming
{
    private readonly HBoxContainer _root;
    private readonly LineEdit _name;
    private readonly Dropdown _pick;
    private const float PickWidth = 17;
    private readonly Button _confirm;
    private readonly Func<ChangeList> _changes;
    private readonly Action<string> _say;
    private bool _replacing;

    /// <summary>
    /// A hidden row; <paramref name="suffix"/> ends its nodes' names, <paramref name="changes"/> gives what is saved
    /// and <paramref name="say"/> shows each outcome. The field is <paramref name="width"/> wide, or takes the row's
    /// width when it is 0.
    /// </summary>
    public PresetNaming(string suffix, Func<ChangeList> changes, Action<string> say, float width = 0)
    {
        _changes = changes;
        _say = say;
        _root = Ui.Row(6, "Cs2PresetNaming" + suffix);
        _root.Visible = false;
        // The field and its ▾ as one control in one box: the list fills the field, so there is one name on the row.
        var box = Ui.Add(_root, new PanelContainer());
        box.SizeFlagsHorizontal = width > 0 ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill;
        var frame = (StyleBox)Look.Box(Art.RowBox).Duplicate();
        // The mark stands in the middle of its own bar, which keeps it off the box's right edge.
        frame.ContentMarginLeft = 0;
        frame.ContentMarginRight = 2;
        frame.ContentMarginTop = frame.ContentMarginBottom = 0;
        PixelBox.Panel(box, frame);
        var field = Ui.Add(box, Ui.Row(0));
        _name = new LineEdit
        {
            Name = "Cs2PresetName" + suffix,
            PlaceholderText = Tr.Get("Preset name"),
            MaxLength = 60,
            CustomMinimumSize = new Vector2(width, 0),
            SizeFlagsHorizontal = width > 0 ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill,
        };
        Look.DarkField(_name);
        // The box around the field and its ▾ is the field's: the field itself draws none.
        var bare = new StyleBoxEmpty
        {
            ContentMarginLeft = 10,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        _name.AddThemeStyleboxOverride("normal", bare);
        _name.AddThemeStyleboxOverride("read_only", bare);
        field.AddChild(_name);
        // New, then each preset: picking one fills its name, to save these changes over it. The bar shows no choice
        // of its own: the field says which.
        _pick = new Dropdown("Cs2PresetPick" + suffix, Dropdown.Bare);
        _pick.FixWidth(PickWidth);
        _pick.Under = box;
        _pick.Root.TooltipText = Tr.Get("Update a preset: pick it to save these changes over it");
        _pick.ItemSelected += Picked;
        field.AddChild(_pick.Root);
        _confirm = Ui.Button(Tr.Get("Save"), Look.Button4, "Cs2PresetSave" + suffix, () => Save(_replacing));
        _root.AddChild(_confirm);
        // As wide as its widest word, so Save turning to Update or Replace moves nothing beside it.
        void Fit()
        {
            var font = _confirm.GetThemeFont("font");
            var size = _confirm.GetThemeFontSize("font_size");
            var widest = new[] { Tr.Get("Save"), Tr.Get("Update"), Tr.Get("Replace") }.Max(w =>
                font.GetStringSize(w, fontSize: size).X
            );
            var box = _confirm.GetThemeStylebox("normal").GetMinimumSize().X;
            _confirm.CustomMinimumSize = new Vector2(MathF.Ceiling(widest + box), 0);
        }
        Look.OnTheme(_confirm, Fit);
        Fit();
        _name.TextSubmitted += _ => Save(_replacing);
        // Another name asks afresh whether it replaces anything.
        _name.TextChanged += _ => Replacing(false);
        _name.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                _root.Visible = false;
                _name.AcceptEvent();
            }
        };
        var cancel = Ui.Button("✕", "", "Cs2PresetCancel" + suffix, () => _root.Visible = false);
        cancel.Flat = true;
        cancel.TooltipText = Tr.Get("Cancel");
        _root.AddChild(cancel);
    }

    public HBoxContainer Root => _root;

    /// <summary>
    /// A quiet save icon of <paramref name="size"/> in <paramref name="icons"/> that opens the row, put under
    /// <paramref name="column"/>'s last line, for a place with no line of its own to say things in: what came of a
    /// save is said over the icon, as Export setup's line is.
    /// </summary>
    public static PresetNaming WithIcon(
        string suffix,
        Func<ChangeList> changes,
        Container icons,
        Container column,
        float size
    )
    {
        Button? shown = null;
        var naming = new PresetNaming(
            suffix,
            changes,
            said =>
            {
                if (said.Length > 0 && shown != null)
                    FloatNote.Show(shown, said);
            }
        );
        var icon = IconButton.Quiet(
            Ui.Greyscale(Art.File(Art.SavePath)),
            Tr.Get("Save as preset: keeps this side's changes by name, for any character to use"),
            "Cs2CardSavePreset" + suffix,
            naming.Toggle
        );
        // Never focused: Enter in the name field must not press it again.
        icon.FocusMode = Control.FocusModeEnum.None;
        icon.CustomMinimumSize = new Vector2(size, size);
        // The game's save picture fills its square: drawn smaller, as the copy picture beside it is.
        icon.AddThemeConstantOverride("icon_max_width", 15);
        icons.AddChild(icon);
        column.AddChild(naming.Root);
        naming._icon = shown = icon;
        return naming;
    }

    private Button? _icon;

    /// <summary>Whether there is anything to save: without, the icon is off and the row closes.</summary>
    public void Allow(bool can)
    {
        if (_icon != null)
            _icon.Disabled = !can;
        if (!can)
            _root.Visible = false;
    }

    /// <summary>Shows the row with an empty name to type; a second press hides it.</summary>
    public void Toggle()
    {
        if (_root.Visible)
        {
            _root.Visible = false;
            return;
        }
        _root.Visible = true;
        _pick.Clear();
        _pick.AddItem(Tr.Get("New preset"));
        foreach (var preset in PanelState.Presets().List)
            _pick.AddItem(preset.Name);
        _pick.Select(-1);
        Replacing(false);
        _name.Text = "";
        // Once the row is laid out: a field shown this frame can't take the focus yet.
        _name.CallDeferred(Control.MethodName.GrabFocus);
        _say("");
    }

    private void Replacing(bool on, bool picked = false)
    {
        _replacing = on;
        _confirm.Text =
            !on ? Tr.Get("Save")
            : picked ? Tr.Get("Update")
            : Tr.Get("Replace");
    }

    // A preset from the list: its name in the field, and Save reads Update, as it is meant to be saved over.
    private void Picked(int index)
    {
        if (index == 0)
        {
            _name.Text = "";
            Replacing(false);
        }
        else
        {
            _name.Text = PanelState.Presets().List[index - 1].Name;
            Replacing(true, picked: true);
        }
        _pick.Select(-1);
        _name.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Save(bool replace)
    {
        var presets = PanelState.Presets();
        var result = presets.Save(_name.Text, _changes(), replace);
        switch (result)
        {
            case PresetResult.Saved or PresetResult.Replaced:
                PanelState.SavePresets();
                _root.Visible = false;
                var name = presets.Find(_name.Text)!.Name;
                _say(
                    result == PresetResult.Replaced
                        ? Tr.Format("Updated preset: {0}", name)
                        : Tr.Format("Saved as preset: {0}", name)
                );
                break;
            case PresetResult.NameTaken:
                Replacing(true);
                _say(Tr.Get("A preset has this name. Replace it?"));
                break;
            case PresetResult.NoName:
                _say(Tr.Get("Give the preset a name."));
                break;
            case PresetResult.Full:
                _say(Tr.Format("{0} presets already: delete one first.", Presets.Max.ToString(Tr.Culture)));
                break;
            default:
                _say(Tr.Get("No changes to save."));
                break;
        }
    }
}
