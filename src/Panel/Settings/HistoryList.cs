using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>
/// The kept comparisons and runs, newest first, in the Saved runs section, folded away until "Show runs" opens them
/// (and again each time Settings is shown, so they don't lengthen its scroll): each run with its
/// name or setup, changes, targets, date, state, pin and a mark for another build. A selected row shows its actions
/// on a second line, so the list never grows wider than Settings: show it, compare the setup against it, rename it
/// in place, or delete it after one confirm (refused with why for a pinned run, the saved comparison's and the one
/// in progress). Unreadable files come last and can be deleted.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class HistoryList
{
    private const float NameWidth = 260;

    private bool _expanded;

    private readonly VBoxContainer _root;
    private readonly Label _count;
    private readonly Label _note;
    private readonly Button _toggle;
    private readonly VBoxContainer _rows;
    private readonly VBoxContainer _comparisons;
    private readonly VBoxContainer _comparisonRows;
    private int _kept;
    private readonly Label _empty;
    private readonly Label _message;
    private string? _selected;
    private string? _renaming;
    private string? _confirming;
    private int _shown;
    private int _total;
    private HistoryStamp? _built;

    public HistoryList()
    {
        _root = Ui.Column(8, "Cs2History");
        var head = Ui.Add(_root, Ui.Row(10));
        var text = Ui.Add(head, Ui.Column(2));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _count = Ui.Label("", TextStyle.BodyStrong, "Cs2HistoryCount");
        text.AddChild(_count);
        _note = Ui.Label("", TextStyle.Tiny, wrap: true);
        text.AddChild(_note);
        _toggle = Ui.Button("", Look.Button8, "Cs2HistoryToggle", () => SetExpanded(!_expanded));
        _toggle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_toggle);
        _message = Ui.Label("", TextStyle.Small, wrap: true);
        _message.Visible = false;
        _root.AddChild(_message);
        _comparisons = Ui.Add(_root, Ui.Column(4, "Cs2HistoryComparisons"));
        _comparisons.AddChild(Ui.Label(Tr.Get("Comparisons"), TextStyle.BodyStrong));
        _comparisons.AddChild(
            Ui.Label(
                Tr.Format("The last {0} comparisons are kept, with their runs.", KeptComparisons.Max),
                TextStyle.Tiny
            )
        );
        _comparisonRows = Ui.Add(_comparisons, Ui.Column(4));
        _rows = Ui.Add(_root, Ui.Column(6, "Cs2HistoryList"));
        _empty = Ui.Label(
            Tr.Get("No runs yet. Simulate once and it appears here."),
            TextStyle.Body,
            "Cs2HistoryEmpty",
            wrap: true
        );
        _root.AddChild(_empty);
    }

    public Control Root => _root;

    /// <summary>Raised when Show is chosen and the run is now the shown result.</summary>
    public event Action? Shown;

    /// <summary>Raised with the run's id when Compare is chosen, to compare the setup now against it.</summary>
    public event Action<string>? CompareChosen;

    /// <summary>Opens or folds the list of runs.</summary>
    public void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        Rebuild();
    }

    /// <summary>Folds the list away, if it is open.</summary>
    public void Fold()
    {
        if (_expanded)
            SetExpanded(false);
    }

    /// <summary>
    /// Lists the kept runs again if they changed since the rows were made. Opening the window or switching to
    /// Settings asks every time, and making a hundred rows again took most of a frame.
    /// </summary>
    public void Refresh()
    {
        if (HistoryStamp.Of(RunStore.Shared, _expanded) != _built)
            Rebuild();
    }

    // Makes the rows again: after anything the list itself changed (a selection, a rename under way, a confirm).
    private void Rebuild()
    {
        Ui.Clear(_rows);
        var store = RunStore.Shared;
        _built = HistoryStamp.Of(store, _expanded);
        var (headers, unreadable) = store.ListAll();
        var comparison = store.Pinned();
        var pinned = headers.Count(h => h.Pinned || comparison.Contains(h.Id));
        _total = headers.Count + unreadable.Count;
        _count.Text = Tr.Format("Kept {0} of {1} · {2} pinned", [headers.Count, store.Max, pinned]);
        _note.Text =
            pinned > store.Max
                ? Tr.Get(
                    "More runs are pinned or in kept comparisons than the limit: they stay until unpinned or removed."
                )
                : Tr.Get("Pinned runs and kept comparisons' runs stay past the limit. Select a run for actions.");
        _kept = Simulate.RecentComparisons.Fill(
            _comparisonRows,
            "Cs2HistoryCmp",
            () =>
            {
                PanelState.ShowComparison(true);
                Shown?.Invoke();
            },
            Rebuild,
            Say
        );
        _comparisons.Visible = _expanded && _kept > 0;
        _empty.Visible = _total == 0;
        _toggle.Visible = _total > 0;
        _toggle.Text = _expanded
            ? Tr.Get("Hide runs ▴")
            : Tr.Plural("Show {0} run ▾", "Show {0} runs ▾", _total, _total);
        _rows.Visible = _expanded;
        _message.Visible = _expanded && _message.Text.Length > 0;
        _shown = 0;
        if (!_expanded)
            return;
        foreach (var header in headers)
            _rows.AddChild(Row(header));
        foreach (var file in unreadable)
            _rows.AddChild(Unreadable(file));
    }

    /// <summary>Describes what the history shows, for <c>cs2 ui</c>.</summary>
    public void Describe(System.Text.StringBuilder text)
    {
        text.Append("\nhistory_open=").Append(_expanded ? "yes" : "no");
        text.Append("\nhistory_runs=").Append(_total).Append(" listed=").Append(_shown);
        text.Append("\nhistory_selected=").Append(_selected ?? "none");
        text.Append("\nhistory_rename=").Append(_renaming ?? "none");
        text.Append("\nhistory_confirm=").Append(_confirming ?? "none");
        text.Append("\nhistory_count=").Append(_count.Text);
        text.Append("\nhistory_comparisons=").Append(_kept);
    }

    private void Say(string? text)
    {
        _message.Text = text ?? "";
        _message.Visible = text != null;
    }

    private string CurrentEdit() =>
        _rows.FindChild("Cs2HistoryRename", recursive: true, owned: false) is LineEdit edit ? edit.Text : "";

    private void Pin(RunHeader header) => Act(() => RunService.Instance.PinRun(header.Id, !header.Pinned));

    private void Rename(RunHeader header, string name)
    {
        _renaming = null;
        Act(() => RunService.Instance.RenameRun(header.Id, name));
    }

    private void Delete(RunHeader header)
    {
        _confirming = null;
        try
        {
            if (RunService.Instance.DeleteRun(header.Id) is { } refusal)
                Say(Capitalised(Tr.Note(refusal)));
            else
            {
                _selected = null;
                Say(null);
            }
        }
        catch (CommandException e)
        {
            Log.Warn("saved runs", e);
            Say(e.Message);
        }
        Rebuild();
    }

    private void Show(RunHeader header)
    {
        try
        {
            RunService.Instance.ShowSaved(header.Id);
            PanelState.SetCompareWith(null);
            Shown?.Invoke();
        }
        catch (CommandException e)
        {
            Log.Warn("saved runs", e);
            Say(e.Message);
            Rebuild();
        }
    }

    private void Act(Action action)
    {
        try
        {
            action();
            Say(null);
        }
        catch (CommandException e)
        {
            Log.Warn("saved runs", e);
            Say(e.Message);
        }
        Rebuild();
    }
}
