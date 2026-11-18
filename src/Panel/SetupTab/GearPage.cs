using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// Gear and levels for the side being set up: its combat levels on the left, and the game's own combat loadout
/// column (equipment, toolbelt, stance, prayers, food, spell, rune costs, drawn with the game's slot frames and titles)
/// on the right. A changed control shows what the side had ("was 55"); a change the game refuses shows its reason
/// beside it. Edits go to the side in <see cref="PanelState"/>, which is checked again once they settle. Read only
/// while a run goes. Main thread.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class GearPage
{
    private const int SlotSize = 56;
    private const int SmallSlot = 44;
    private const int PrayerSlot = 36;
    private const int ColumnWidth = 260;

    private readonly HFlowContainer _root;
    private readonly ItemPicker _picker;
    private readonly VBoxContainer _levels;
    private readonly VBoxContainer _loadoutA;
    private readonly VBoxContainer _loadoutB;
    private string? _shownFor;

    // Whether a side has been drawn: what the page keeps showing while the next is read.
    private bool _drawn;
    private bool _readOnly;
    private SideLetter _letter;
    private SideState _side = SideState.AsNow;

    public GearPage(ItemPicker picker)
    {
        _picker = picker;
        // The levels beside the loadout where they fit, under it where they don't: lines fill from the bottom up. As
        // wide as the page's scroll area, since a flow given only its least width would always wrap.
        _root = new HFlowContainer
        {
            Name = "Cs2GearPage",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ReverseFill = true,
        };
        _root.AddThemeConstantOverride("h_separation", 16);
        _root.AddThemeConstantOverride("v_separation", 16);
        _levels = Ui.Add(_root, Ui.Column(6, "Cs2Levels"));
        _levels.CustomMinimumSize = new Vector2(330, 0);
        var loadout = Ui.Add(_root, new PanelContainer { Name = "Cs2Loadout" });
        PixelBox.Panel(loadout, NativeStyles.Column);
        var columns = Ui.Add(loadout, Ui.Row(16));
        _loadoutA = Ui.Add(columns, Ui.Column(10));
        _loadoutA.CustomMinimumSize = new Vector2(ColumnWidth, 0);
        _loadoutB = Ui.Add(columns, Ui.Column(10));
        _loadoutB.CustomMinimumSize = new Vector2(ColumnWidth, 0);
    }

    // Opens the picker again on what it was last opened for, from a newer view of the side.
    private Action<SideView>? _pickAgain;

    public Control Root => _root;

    /// <summary>
    /// Shows <paramref name="side"/> from <paramref name="check"/>, its latest check (for its changes, or for earlier
    /// ones until the next answers); redraws only when something it shows changed.
    /// </summary>
    public void Show(SideLetter letter, SideState side, SideCheck? check, bool readOnly)
    {
        var key = string.Join(
            "|",
            letter,
            side.Changes.ShortForm,
            check is null ? "-" : check.Changes.Changes.ShortForm + "@" + check.Changes.Setup,
            check?.GetHashCode(),
            readOnly,
            Look.Kind
        );
        if (key == _shownFor)
            return;
        _shownFor = key;
        _readOnly = readOnly;
        _letter = letter;
        _side = side;
        // A side not read yet: the page keeps what it last drew, faded and out of reach, or stands as its
        // skeleton the first time. Either way it has its shape, and nothing moves when the side arrives.
        var waiting = check == null || LiveGame.Find() == null;
        Pending.Pulse(_root, waiting);
        _root.MouseBehaviorRecursive = waiting
            ? Control.MouseBehaviorRecursiveEnum.Disabled
            : Control.MouseBehaviorRecursiveEnum.Inherited;
        if (waiting && _drawn)
            return;
        Ui.Clear(_levels);
        Ui.Clear(_loadoutA);
        Ui.Clear(_loadoutB);
        if (waiting)
        {
            if (LiveGame.Find() != null)
                GearSkeleton.Fill(_levels, _loadoutA, _loadoutB, SlotSize, SmallSlot);
            return;
        }
        _drawn = true;
        var view = new SideView(check!.Changes, side.Changes);
        Levels(view);
        Equipment(view);
        Toolbelt(view);
        Stance(view);
        Prayers(view);
        Food(view);
        Spell(view);
        Runes(view);
        // The picker left open over a side that just changed is filled again from it: what it says the side has
        // now, and the stats it compares against.
        if (_picker.Open)
            _pickAgain?.Invoke(view);
    }

    private static string Name(string id) => PanelState.Names()(id);

    // Marked, for the picker's line that names the side.
    private string Letter => _letter == SideLetter.A ? SideLine.A : SideLine.B;

    // Puts a change in the side's list, or takes its key out when it would set what the side's base already has.
    private void Edit(SideView view, Change change, bool asBase) =>
        SetChanges(asBase ? view.Changes.Without(change.Key) : view.Changes.With(change));

    private void SetChanges(ChangeList changes) => PanelState.SetSide(_letter, _side.WithChanges(changes));

    // A control's hover: its name, what the side had before a change, and the requirement ignored for it.
    private static string Hover(string name, ChangeOutcome? outcome, string? was, IReadOnlyList<Note>? unmet = null)
    {
        var lines = new List<string> { name };
        if (was != null)
            lines.Add(Marks.Muted(Tr.Format("was {0}", was)));
        if (outcome?.State == ChangeState.RequirementIgnored)
            lines.Add(Tr.Format("Requirement ignored: {0}", Tr.Notes(outcome.Reasons, "; ")));
        if (unmet is { Count: > 0 })
            // Translators: Hover of a gear slot whose item the setup's levels or unlocks don't allow; {0} is what it needs.
            lines.Add(Marks.Bad(Tr.Format("Can't be worn: {0}", Tr.Notes(unmet, "; "))));
        return string.Join("\n", lines);
    }

    // What the shown item needs and the side lacks, only while the side keeps to requirements: ignoring them, it is
    // worn whatever it needs.
    private IReadOnlyList<Note> Unmet(SideView view, string slot, bool toolbelt = false) =>
        _side.IgnoreRequirements ? [] : view.Unmet(slot, toolbelt);

    private IReadOnlyList<Note> PrayerUnmet(SideView view, string? prayer) =>
        _side.IgnoreRequirements || prayer == null ? [] : view.PrayerUnmet(prayer);
}
