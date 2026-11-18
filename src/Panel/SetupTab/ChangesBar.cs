using System.Globalization;
using System.Text;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// The side's changes from every page, once each: a chip per change ("Defence 55 → 60") with ✕ to undo it wherever
/// it was made, edged amber when a requirement was ignored for it (the reason on hover); then Clear changes, Save as
/// preset… (a name, and whether to replace a preset of that name) and Export setup. A change the game refuses isn't
/// kept, so none is listed here. The chips fold away under their title, the buttons staying.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class ChangesBar
{
    private readonly PanelContainer _root;
    private readonly HFlowContainer _chips;
    private readonly SideLine _title = new(TextStyle.Label);
    private readonly Control _chipsHolder;
    private readonly Button _fold;
    private readonly LineEdit _search;

    // Each chip with its words, so the search hides the others without remaking them.
    private readonly List<(Control Chip, string Text)> _shown = [];

    // Kept for the session: the bar is remade with each page, and folding is a choice about every page but Slayer's,
    // which starts folded: its masters need the room, and its changes show on their rows.
    private static bool _folded;
    private static bool _slayerFolded = true;

    private bool Folded => _page == SetupPage.Slayer ? _slayerFolded : _folded;
    private readonly Button _clear;
    private readonly Button _clearPage;
    private string _page = SetupPage.Gear;
    private readonly Button _save;
    private readonly Button _copy;
    private readonly PresetNaming _naming;
    private readonly Label _said;
    private SideCheck? _check;
    private string? _shownFor;
    private SideLetter _letter;
    private SideState _side = SideState.AsNow;

    public ChangesBar()
    {
        _root = Look.Panel(Art.RowBox, "Cs2ChangesBar");
        var column = Ui.Add(_root, Ui.Column(8));
        column.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var header = Ui.Add(column, Ui.Row(8));
        _title.Root.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _title.Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(_title.Root);
        _fold = IconButton.Quiet(null, "", "Cs2ChangesFold", () => Fold(!Folded));
        header.AddChild(_fold);
        // The whole title row folds and opens the list, not only its arrow.
        header.MouseFilter = Control.MouseFilterEnum.Stop;
        header.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        header.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                Fold(!Folded);
        };
        _search = new LineEdit
        {
            Name = "Cs2ChangesSearch",
            // Translators: Placeholder of the search over the side's changes on the Setup tab.
            PlaceholderText = Tr.Get("Search changes"),
            ClearButtonEnabled = true,
            RightIcon = Art.File(Art.SearchBarPath),
        };
        Look.DarkField(_search);
        _search.TextChanged += _ => Filter();
        column.AddChild(_search);
        // Open, the chips have half the side column (the effects the other half) and scroll past it.
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        QuietScrollBar.Apply(scroll);
        var holder = ScrollFade.Wrap(scroll);
        holder.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        // Out to the bar's own edge, past its padding: the scrollbar then stands under the page's.
        holder.AddThemeConstantOverride("margin_right", -(int)_root.GetThemeStylebox("panel").GetMargin(Side.Right));
        column.AddChild(holder);
        _chipsHolder = holder;
        _chips = Ui.Add(scroll, new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _chips.AddThemeConstantOverride("h_separation", 8);
        _chips.AddThemeConstantOverride("v_separation", 8);
        _clear = Ui.Button(
            Tr.Get("Clear changes"),
            Look.Button4,
            "Cs2ChangesClear",
            () => PanelState.SetSide(_letter, _side.WithChanges(ChangeList.Empty))
        );
        _clearPage = Ui.Button(
            Tr.Get("Clear this page"),
            Look.Button4,
            "Cs2ChangesClearPage",
            () => PanelState.SetSide(_letter, _side.WithChanges(SetupPage.WithoutPage(_page, _side.Changes)))
        );
        // Wrapping in the narrow side column.
        var actions = Ui.Add(column, new HFlowContainer());
        actions.AddThemeConstantOverride("h_separation", 8);
        actions.AddThemeConstantOverride("v_separation", 6);
        actions.AddChild(_clear);
        actions.AddChild(_clearPage);
        _naming = new PresetNaming("", () => _side.Changes, Say, 260);
        _save = Ui.Button(Tr.Get("Save as preset…"), Look.Button4, "Cs2SavePreset", _naming.Toggle);
        _save.TooltipText = Tr.Get("Keep these changes by name, for any character to use");
        actions.AddChild(_save);
        _copy = Ui.Button(Tr.Get("Export setup"), Look.Button4, "Cs2CopySetup", CopySetup);
        actions.AddChild(_copy);
        _said = Ui.Add(actions, Ui.Label("", TextStyle.Small, "Cs2ChangesSaid"));
        _said.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        // The preset's name, under the buttons while one is being saved.
        column.AddChild(_naming.Root);
    }

    private void Fold(bool folded)
    {
        if (_page == SetupPage.Slayer)
            _slayerFolded = folded;
        else
            _folded = folded;
        ShowFold();
    }

    // Shows the chips whose words hold every word searched for, in any case.
    private void Filter()
    {
        var words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (chip, text) in _shown)
            chip.Visible = words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    private void ShowFold()
    {
        _search.Visible = !Folded && _shown.Count > 1;
        _root.SizeFlagsVertical = Folded ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill;
        _chipsHolder.Visible = !Folded;
        _fold.Icon = Art.File(Folded ? Art.ClosedArrowPath : Art.OpenArrowPath);
        _fold.TooltipText = Folded ? Tr.Get("Show changes") : Tr.Get("Hide changes");
    }

    private void CopySetup()
    {
        if (_check != null)
            FloatNote.Show(_copy, Sharing.ExportSide(_check) ?? Sharing.Exported);
    }

    private void Say(string text) => _said.Text = text;

    public Control Root => _root;

    /// <summary>
    /// Shows <paramref name="side"/>'s changes with what <paramref name="check"/> made of them;
    /// <paramref name="alone"/> when it is the only side.
    /// </summary>
    public void Show(SideLetter letter, SideState side, SideCheck? check, bool readOnly, bool alone, string page)
    {
        var key = string.Join("|", letter, side.Changes.ShortForm, check?.GetHashCode(), readOnly, alone, page);
        if (key == _shownFor)
            return;
        _shownFor = key;
        if (letter != _letter)
        {
            _naming.Root.Visible = false;
            Say("");
        }
        _letter = letter;
        _side = side;
        _check = check;
        Ui.Clear(_chips);
        _shown.Clear();
        var name = letter == SideLetter.A ? "A" : "B";
        // The side is named only when there are two.
        if (alone)
            _title.Set(Tr.Get("Changes ({0})"), side.Changes.Count.ToString(Tr.Culture));
        else
            _title.Set(Tr.Get("Changes on {0} ({1})"), name, side.Changes.Count.ToString(Tr.Culture));
        _page = page;
        ShowFold();
        _clear.Disabled = readOnly || side.Changes.IsEmpty;
        var onPage = SetupPage.ChangeCount(page, side.Changes);
        _clearPage.Disabled = readOnly || onPage == 0;
        _clearPage.TooltipText =
            onPage == 0
                ? Tr.Get("No changes were made on this page")
                : Tr.Plural(
                    "Undo the {0} change made on this page",
                    "Undo the {0} changes made on this page",
                    onPage,
                    onPage.ToString(Tr.Culture)
                );
        _save.Disabled = readOnly || side.Changes.IsEmpty;
        // The line is the side as its check found it: there is nothing to copy before the check is in.
        _copy.Disabled = check == null;
        _copy.TooltipText =
            check == null
                ? Tr.Get("Checking this side…")
                : Tr.Get("Copies this side as one line, for another player to import");
        if (side.Changes.IsEmpty)
        {
            _chips.AddChild(Ui.Label(Tr.Get("None yet"), TextStyle.Small));
            return;
        }
        var view = check != null ? new SideView(check.Changes, side.Changes) : null;
        var names = PanelState.Names();
        var n = 0;
        foreach (var change in side.Changes)
        {
            n++;
            var at = n.ToString(CultureInfo.InvariantCulture);
            var outcome = view?.Outcome(change);
            var from = outcome?.From ?? view?.From(change);
            var text = Tr.Note(ChangeNotes.Describe(change, from, names));
            var undo = side.Changes.Without(change.Key);
            var chip = Chip.Make(
                "Cs2Change" + at,
                text,
                "Cs2ChangeUndo" + at,
                readOnly ? null : () => PanelState.SetSide(_letter, _side.WithChanges(undo)),
                Hover(text, outcome)
            );
            Chip.SetEdge(
                chip,
                outcome?.State switch
                {
                    ChangeState.RequirementIgnored => Look.Risky,
                    _ => Look.Safe,
                }
            );
            _chips.AddChild(chip);
            _shown.Add((chip, Marks.Plain(text)));
        }
        ShowFold();
        Filter();
    }

    // The change, then what the check made of it when that wasn't simply applied.
    private static string Hover(string text, ChangeOutcome? outcome) =>
        outcome?.State switch
        {
            null => text + "\n" + Marks.Muted(Tr.Get("checking…")),
            ChangeState.NoChange => text + "\n" + Tr.Get("No change: the side already has this."),
            ChangeState.RequirementIgnored => text
                + "\n"
                + Tr.Format("Requirement ignored: {0}", Tr.Notes(outcome.Reasons, "; ")),
            _ => text,
        };

    /// <summary>The side's changes as <c>cs2 ui</c> lists them: <c>side_change a|b &lt;n&gt; &lt;short form&gt; &lt;state&gt; [&lt;reason&gt;]</c>.</summary>
    public static void Describe(StringBuilder text)
    {
        foreach (var letter in new[] { SideLetter.A, SideLetter.B })
        {
            if ((letter == SideLetter.A ? PanelState.A : PanelState.B) is not { CanChange: true } side)
                continue;
            var check = letter == SideLetter.A ? PanelState.CheckA : PanelState.CheckB;
            var n = 0;
            foreach (var change in side.Changes)
            {
                n++;
                var outcome = check?.Changes.Outcomes.FirstOrDefault(o => o.Change == change);
                text.Append("\nside_change ")
                    .Append(letter == SideLetter.A ? 'a' : 'b')
                    .Append(' ')
                    .Append(n.ToString(CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(change.ShortForm)
                    .Append(' ')
                    .Append(
                        outcome?.State switch
                        {
                            null => "checking",
                            ChangeState.Accepted => "accepted",
                            ChangeState.NoChange => "no-change",
                            ChangeState.Refused => "refused",
                            ChangeState.NotInBuild => "not-in-build",
                            _ => "requirement-ignored",
                        }
                    );
                if (outcome is { Reasons.Count: > 0 })
                    text.Append(' ').Append(outcome.Reasons.English("; "));
            }
        }
    }
}
