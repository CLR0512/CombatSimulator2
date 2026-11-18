using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Stats;

/// <summary>
/// Every stat and every modifier with its source, over the whole panel: for A and B with B − A, or one side. Read from
/// the sides' latest checks, the same reading their cards and key stats show. Rows are neutral; only a difference is
/// coloured. Only differences narrows both lists; the modifiers fold by group and a search narrows them.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class FullStatsView
{
    public const string Both = "both";
    public const string OnlyA = "a";
    public const string OnlyB = "b";

    private const float ModifiersWidth = 600;

    private readonly VBoxContainer _root;
    private readonly Button _back;
    private readonly Segmented _sides;
    private readonly Button _onlyDiff;
    private readonly Label _note;

    // What is drawn: the sheets and the choices they were drawn with, so a frame with nothing new does nothing.
    private (SetupSheet? A, SetupSheet? B, string Show, bool Only) _drawn;
    private IReadOnlyList<SetupSheet> _sheets = [];

    public FullStatsView()
    {
        _root = Ui.Column(10, "Cs2StatsView");
        _root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var head = Ui.Row(10, "Cs2StatsHead");
        // Off the window's edges, as far as the Simulate tab's setup column is.
        _root.AddChild(Ui.Margin(head, 12, 0, 12, 0));
        _back = Ui.Button("", Look.Button4, "Cs2StatsBack", () => Cs2Modal.ShowView(From));
        head.AddChild(_back);
        var title = Ui.Label(Tr.Get("Stats and modifiers"), TextStyle.Heading);
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(title);
        _note = Ui.Label("", TextStyle.Tiny, "Cs2StatsNote");
        _note.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _note.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _note.ClipText = true;
        head.AddChild(_note);
        _sides = new Segmented(
            "Cs2StatsSides",
            [
                (Both, Tr.Format("{0} and {1}", SideLine.A, SideLine.B), "Cs2StatsBoth"),
                (OnlyA, SideLine.A, "Cs2StatsA"),
                (OnlyB, SideLine.B, "Cs2StatsB"),
            ],
            show =>
            {
                Sides = show;
                Refresh();
            }
        );
        head.AddChild(_sides.Root);
        _onlyDiff = Ui.Button(Tr.Get("Only differences"), Look.Button4, "Cs2StatsOnlyDiff");
        _onlyDiff.ToggleMode = true;
        _onlyDiff.Toggled += only =>
        {
            PanelState.Prefs.StatsOnlyDifferences = only;
            PanelState.Save();
            Refresh();
        };
        head.AddChild(_onlyDiff);
        head.AddChild(Ui.Button(Tr.Get("Copy as text"), Look.Button4, "Cs2StatsCopy", Copy));

        var body = Ui.Add(_root, Ui.Row(10, "Cs2StatsBody"));
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(BuildStats());
        var mods = BuildModifiers();
        mods.CustomMinimumSize = new Vector2(ModifiersWidth, 0);
        body.AddChild(mods);
    }

    public Control Root => _root;

    /// <summary>The view Back returns to: Simulate or Setup.</summary>
    public static string From { get; private set; } = Cs2Modal.Simulate;

    /// <summary>Which sides are shown while comparing: both, a or b.</summary>
    public static string Sides { get; private set; } = Both;

    /// <summary>
    /// Opens the view over the one shown now, which Back returns to; <paramref name="sides"/> picks both, a or b while
    /// comparing.
    /// </summary>
    public static void Open(string? sides = null)
    {
        if (PanelState.Prefs.View != Cs2Modal.Stats)
            From = PanelState.Prefs.View == Cs2Modal.Setup ? Cs2Modal.Setup : Cs2Modal.Simulate;
        if (sides != null)
            Sides = sides;
        Cs2Modal.Jump(Cs2Modal.Stats);
    }

    // The sheets on show and what is still being read; null sheets while a side's check is on its way.
    private static (SetupSheet? A, SetupSheet? B, bool HasB, bool SavedRun) Shown()
    {
        var b = PanelState.B;
        return (
            PanelState.CheckA?.Sheet,
            b is { CanChange: true } ? PanelState.CheckB?.Sheet : null,
            b is { CanChange: true },
            b is { CanChange: false }
        );
    }

    /// <summary>Draws again when the sides' checks or the choices changed; does nothing otherwise.</summary>
    public void Refresh()
    {
        var (a, b, hasB, savedRun) = Shown();
        var show = hasB ? Sides : OnlyA;
        var only = PanelState.Prefs.StatsOnlyDifferences;
        _back.Text = From == Cs2Modal.Setup ? Tr.Get("◂ Back to Setup") : Tr.Get("◂ Back to results");
        _sides.Root.Visible = hasB;
        _sides.Show(show);
        _onlyDiff.SetPressedNoSignal(only);
        var drawn = (a, b, show, only);
        if (drawn == _drawn)
            return;
        var waiting =
            LiveGame.Find() == null ? Tr.Get("Load a character to see its setup.")
            : a == null || (hasB && b == null) ? Tr.Get("Reading the setup…")
            : null;
        if (waiting != null)
        {
            SideLine.OnLabel(_note, waiting, TextStyle.Tiny);
            return;
        }
        _drawn = drawn;
        _sheets =
            show == Both ? [a!, b!]
            : show == OnlyB ? [b!]
            : [a!];
        var two = _sheets.Count == 2;
        _onlyDiff.Visible = two;
        var note =
            savedRun ? Tr.Format("A saved run keeps no stats: side {0} as it is now.", SideLine.A)
            : two ? $"{SideLine.A} {Words.Setup(a!.Setup)} · {SideLine.B} {Words.Setup(b!.Setup)}"
            : Tr.Get("as the game computes them · read from a copy, your game is untouched");
        SideLine.OnLabel(_note, note, TextStyle.Tiny);
        DrawStats();
        DrawModifiers();
    }

    // The shown stats and modifiers as plain lines, for a message or a bug report.
    private void Copy()
    {
        if (_sheets.Count == 0)
            return;
        Clipboard.Set(StatsText.Of(_sheets, PanelState.Prefs.StatsOnlyDifferences, _search.Text));
    }

    /// <summary>The sides shown, the counts and the search, for <c>cs2 ui</c>.</summary>
    public void Describe(System.Text.StringBuilder text)
    {
        text.Append("\nstats_from=").Append(From);
        text.Append("\nstats_sides=").Append(_sheets.Count == 2 ? Both : _drawn.Show);
        text.Append(" only_diff=").Append(PanelState.Prefs.StatsOnlyDifferences ? "on" : "off");
        text.Append("\nstats_rows=").Append(_statRows).Append(" differing=").Append(_differing);
        text.Append("\nmodifiers=").Append(_modTotal).Append(" listed=").Append(_entries.Count(e => e.Side != null));
        text.Append(" search=").Append(_search.Text);
    }
}
