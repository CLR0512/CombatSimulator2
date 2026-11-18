using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// Any page of entries the game keeps for a side: stars, guild ranks, slayer tasks, shop purchases, quests. Under the
/// build's lock text when it keeps the page from the player, a filter (every entry, or those with modifiers or unlocks)
/// and the legend, the game's groups each with what the side has of it, and their entries: a toggle, − value +, or the
/// value alone on a read-only page; what an entry needs or why it can't change; "was" where
/// the side changed it; its modifiers on hover. Only the rows in view exist, so a page of hundreds builds at once.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class EntriesPage
{
    private const string All = "all";
    private const string Notable = "notable";

    // A heading (no entry) or an entry of a group.
    private readonly record struct Row(PageGroup Group, PageEntry? Entry);

    private readonly VBoxContainer _root;
    private readonly Label _gate;
    private readonly SlayerTaskChoice _slayerTask = new();
    private readonly SlayerMasters _masters = new();

    /// <summary>The Slayer page's goal, for the tab to place beside the page's subtitle.</summary>
    public Control SlayerGoal => _masters.Goal;
    private readonly Segmented _filter;
    private readonly HBoxContainer _legend;
    private readonly VirtualRows _list;
    private readonly HBoxContainer _head;
    private readonly Control _listHolder;
    private readonly Label _none;
    private readonly List<Row> _rows = [];
    private readonly Dictionary<string, string> _filters = new(StringComparer.Ordinal);
    private string? _shownFor;
    private SetupPage? _page;
    private SideLetter _letter;
    private SideState _side = SideState.AsNow;
    private SideCheck? _check;
    private bool _readOnly;

    // Per entry others need (a quest before another): the entries that need it.
    private readonly Dictionary<ChangeKey, List<PageEntry>> _neededBy = [];

    public EntriesPage()
    {
        _root = Ui.Column(8, "Cs2EntriesPage");
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _gate = Ui.Label("", TextStyle.Small, "Cs2PageGate", wrap: true);
        _gate.Modulate = Look.Risky;
        _root.AddChild(_slayerTask.Root);
        _root.AddChild(_masters.Root);
        _root.AddChild(_gate);
        var head = _head = Ui.Add(_root, Ui.Row(12));
        _filter = new Segmented(
            "Cs2PageFilter",
            [(All, "", "Cs2PageFilter0"), (Notable, Tr.Get("With modifiers or unlocks"), "Cs2PageFilter1")],
            chosen =>
            {
                if (_page != null)
                    _filters[_page.Id] = chosen;
                _shownFor = null;
                Draw();
            }
        );
        head.AddChild(_filter.Root);
        _legend = PageLegend.Make(ChangedBox());
        head.AddChild(_legend);
        _list = new VirtualRows("Cs2PageEntries", 44, MakeRow, BindRow);
        _list.Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _none = Ui.Label("", TextStyle.Small, "Cs2PageNone");
        _none.Visible = false;
        _root.AddChild(_none);
        QuietScrollBar.Apply(_list.Root);
        _listHolder = ScrollFade.Wrap(_list.Root);
        _root.AddChild(_listHolder);
    }

    public Control Root => _root;

    /// <summary>
    /// Shows <paramref name="page"/> as <paramref name="check"/> read it for the side; redraws only when the page, the
    /// side's changes or the run state changed.
    /// </summary>
    public void Show(SetupPage page, SideLetter letter, SideState side, SideCheck? check, bool readOnly)
    {
        _page = page;
        _letter = letter;
        _side = side;
        _check = check;
        _readOnly = readOnly || page.State != PageState.Changeable;
        _slayerTask.Root.Visible = page.Id == SetupPage.Slayer;
        _slayerTask.Show(letter, side, readOnly);
        _masters.Root.Visible = page.Id == SetupPage.Slayer && page.State != PageState.NoData;
        if (_masters.Root.Visible)
            _masters.Show(letter, side);
        _neededBy.Clear();
        foreach (var entry in page.Groups.SelectMany(g => g.Entries))
        foreach (var need in entry.After ?? [])
        {
            if (!_neededBy.TryGetValue(need.On, out var needers))
                _neededBy[need.On] = needers = [];
            needers.Add(entry);
        }
        Draw();
    }

    private void Draw()
    {
        if (_page is not { } page)
            return;
        var filter =
            _filters.GetValueOrDefault(page.Id) ?? (page.Id is SetupPage.Shop or SetupPage.Pets ? Notable : All);
        // Spoiler guard's state too: turning it on or off masks or names the Pets page's rows.
        var key = string.Join(
            "|",
            page.Id,
            _letter,
            _side.Changes.ShortForm,
            _check?.GetHashCode(),
            _readOnly,
            filter,
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Spoilers.Current)
        );
        if (key == _shownFor)
            return;
        _shownFor = key;
        // The Slayer page is CS2's own choices alone: no entries, filter or list under them.
        var entriesShown = page.Id != SetupPage.Slayer || page.State == PageState.NoData;
        _head.Visible = entriesShown;
        _listHolder.Visible = entriesShown;
        _gate.Visible = page.Gated || page.Reason != null;
        _gate.Text =
            page.State == PageState.ReadOnly && page.Reason is { } reason ? Tr.Note(reason)
            : page.Gated ? "🔒 " + Tr.Notes(page.Gate, "; ")
            : page.Reason is { } note ? Tr.Note(note)
            : "";
        // A changeable page's own note is plain; a lock or a read-only page is a warning.
        _gate.Modulate = page.Gated || page.State == PageState.ReadOnly ? Look.Risky : Colors.White;
        var entries = page.Groups.Sum(g => g.Entries.Count);
        _filter.Button(All).Text = Tr.Format("All {0}", entries);
        _filter.Show(filter);
        _legend.Visible = page.State == PageState.Changeable;
        _rows.Clear();
        foreach (var group in page.Groups)
        {
            var shown = group.Entries.Where(e => filter == All || e.Notable).ToList();
            if (shown.Count == 0)
                continue;
            _rows.Add(new(group, null));
            foreach (var entry in shown)
                _rows.Add(new(group, entry));
        }
        _list.SetCount(_rows.Count);
        // An empty list says so, and why when it is the filter's doing.
        _none.Visible = _rows.Count == 0 && entriesShown;
        _none.Text =
            filter == Notable && entries > 0 ? Tr.Get("None with modifiers or unlocks") : Tr.Get("Nothing here");
    }

    // "3 of 20 · was 1": how many entries of a group the side has, and the character's count when it differs. A group
    // the game sums up already says the first, so it shows only what the side changed.
    private string Count(PageGroup group)
    {
        // Of what the side can set: an entry that follows another (a guild's bonus) isn't one more thing to have,
        // and a group with a single thing to set (a guild's rank) has nothing to count.
        var settable = group.Entries.Where(e => e.Change != null).ToList();
        if (settable.Count < 2)
            return "";
        var has = settable.Count(e => Value(e) > 0);
        var had = settable.Count(e => e.SaveValue > 0);
        var count = Tr.Format("{0} of {1}", [has, settable.Count]);
        return has != had ? count + " · " + Tr.Format("was {0}", had)
            : group.Summary == null ? count
            : "";
    }

    // The change an entry at value takes: none when the value is the character's own.
    private static ChangeList WithValue(ChangeList changes, PageEntry entry, int value)
    {
        var key = entry.Change!.Value;
        if (value == entry.SaveValue)
            return changes.Without(key);
        var text = key.Kind switch
        {
            ChangeKind.Star => Change.On,
            ChangeKind.Quest => value > 0 ? Change.Done : Change.Open,
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
        return changes.With(new Change(key.Kind, key.Subject, text));
    }

    private void Set(PageEntry entry, int value)
    {
        if (_readOnly || entry.Change == null)
            return;
        if (PageChoice.Is(entry.Change.Value))
        {
            PanelState.SetSide(_letter, _side.WithChanges(PageChoice.With(_page!, _side.Changes, entry, value)));
            return;
        }
        var changes = WithValue(_side.Changes, entry, value);
        if (value == 0 && entry.Change is { } key)
        {
            // What the entry needs stays on, in its own right now: it may have been on only because this one was.
            var kept = (entry.After ?? [])
                .Select(n => _page!.Groups.SelectMany(g => g.Entries).FirstOrDefault(e => e.Change == n.On))
                .Where(e => e != null && Value(e) > 0)
                .ToList();
            // What needs the entry goes off with it: left on, they would turn it straight back on.
            foreach (var needer in Needers(key))
                if (needer.Change is { } theirs)
                    changes = changes.Without(theirs);
            foreach (var before in kept)
                changes = WithValue(changes, before!, 1);
        }
        PanelState.SetSide(_letter, _side.WithChanges(changes));
    }

    // Every entry that needs the entry at key, directly or through another.
    private IEnumerable<PageEntry> Needers(ChangeKey key)
    {
        var seen = new HashSet<ChangeKey> { key };
        var queue = new Queue<ChangeKey>([key]);
        while (queue.Count > 0)
            foreach (var needer in _neededBy.GetValueOrDefault(queue.Dequeue()) ?? [])
                if (needer.Change is { } theirs && seen.Add(theirs))
                {
                    queue.Enqueue(theirs);
                    yield return needer;
                }
    }

    // Whether the side turns on an entry that needs this one: the game then does this one first.
    private bool TurnedOnByNeeder(ChangeKey key) =>
        Needers(key).Any(n => _side.Changes.Find(n.Change!.Value) is { } change && change.Value != Change.Open);

    // The lowest value an entry can be set to: a purchase can't be unbought, a star the character has can't be lost,
    // a technique starts at level 1.
    private static int Lowest(PageEntry entry) =>
        entry.Change?.Kind switch
        {
            ChangeKind.Shop or ChangeKind.Star => entry.SaveValue,
            ChangeKind.Technique => Change.LowestLevel,
            _ => 0,
        };

    // The side's value at once, as Gear and levels shows an edit before its check: the side's change for the entry,
    // on when an entry that needs it is on, the character's own once a change is
    // undone, else what the last check read.
    private int Value(PageEntry entry)
    {
        // An entry that can't be set but hangs on one that can (a guild's bonus on its rank) follows it at once.
        if (entry.Change is not { } key)
            return entry.After is { Count: > 0 } ? (_page!.Unmet(entry, Value).Count == 0 ? 1 : 0) : entry.Value;
        if (_side.Changes.Find(key) is { } change)
        {
            return key.Kind switch
            {
                ChangeKind.Star => 1,
                ChangeKind.Quest => change.Value == Change.Done ? 1 : 0,
                ChangeKind.Potion or ChangeKind.Bonfire => PageChoice.Value(change, entry),
                _ => int.TryParse(change.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                    ? count
                    : entry.Value,
            };
        }
        // An entry others need follows them at once: on while one of them is on, else the character's own.
        if (_neededBy.ContainsKey(key))
            return TurnedOnByNeeder(key) ? Math.Max(1, entry.SaveValue) : entry.SaveValue;
        return _check?.Changes.Changes.Find(key) != null ? entry.SaveValue : entry.Value;
    }
}
