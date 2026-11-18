using System.Globalization;
using System.Text;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Stats;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// What the side being set up does, as it is edited: each stat that differs from the side it is measured against
/// ("5,865 → 6,904", only the difference coloured: green where the side is better), and the modifiers only one of
/// the two has. Side B is measured against side A, side A against side B, and side A alone against itself without
/// its changes, as <c>cs2 effect</c> does. "All 52" opens the full stats view. The latest checks stand while the next
/// is on its way.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class EffectPanel
{
    private const string Differences = "diffs";
    private const string All = "all";

    private readonly VBoxContainer _root;
    private readonly SideLine _title = new(TextStyle.Heading);
    private readonly SideLine _against = new(TextStyle.Small);
    private readonly Segmented _show;
    private readonly Label _state;
    private readonly VBoxContainer _rows;
    private string? _shownFor;
    private Effect _effect = Effect.None;
    private string _stateKey = "none";

    public EffectPanel()
    {
        _root = Ui.Column(8, "Cs2EffectPanel");
        _root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _root.AddChild(_title.Root);
        _root.AddChild(_against.Root);
        _show = new Segmented(
            "Cs2EffectShow",
            [(Differences, "", "Cs2EffectDiffs"), (All, "", "Cs2EffectAll")],
            chosen =>
            {
                // Every stat is the full view's job: the panel stays on the differences.
                if (chosen == All)
                    FullStatsView.Open(PanelState.B is { CanChange: true } ? FullStatsView.Both : FullStatsView.OnlyA);
                _show!.Show(Differences);
            }
        );
        _show.Show(Differences);
        _root.AddChild(_show.Root);
        _state = Ui.Label("", TextStyle.Small, "Cs2EffectState", wrap: true);
        _root.AddChild(_state);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _rows = Ui.Add(scroll, Ui.Column(4));
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        QuietScrollBar.Apply(scroll);
        _root.AddChild(ScrollFade.Wrap(scroll));
    }

    public Control Root => _root;

    /// <summary>Shows the effect of the side being set up; redraws only when a check or the side changed.</summary>
    public void Show(SideLetter letter)
    {
        var a = PanelState.A;
        var b = PanelState.B is { CanChange: true } side ? side : null;
        var name = letter == SideLetter.A ? "A" : "B";
        SetupSheet? other;
        SetupSheet? self;
        string[] against;
        if (letter == SideLetter.B && b != null)
        {
            other = (PanelState.CheckA ?? SideChecker.Latest(SideLetter.A, a))?.Sheet;
            self = (PanelState.CheckB ?? SideChecker.Latest(SideLetter.B, b))?.Sheet;
            against = [Tr.Get("{0} vs {1}"), "B", "A"];
        }
        else if (b != null)
        {
            other = (PanelState.CheckB ?? SideChecker.Latest(SideLetter.B, b))?.Sheet;
            self = (PanelState.CheckA ?? SideChecker.Latest(SideLetter.A, a))?.Sheet;
            against = [Tr.Get("{0} vs {1}"), "A", "B"];
        }
        else
        {
            other = a.Changes.IsEmpty ? null : SideChecker.BaseOfA?.Sheet;
            self = (PanelState.CheckA ?? SideChecker.Latest(SideLetter.A, a))?.Sheet;
            // One setup: no letters, there is nothing to tell it apart from.
            against = [Tr.Get("Against your character unchanged")];
        }
        var checking =
            SideChecker.Checking(letter)
            || (b != null && SideChecker.Checking(letter == SideLetter.A ? SideLetter.B : SideLetter.A));
        var key = string.Join(
            "|",
            letter,
            b != null,
            other?.GetHashCode(),
            self?.GetHashCode(),
            checking,
            a.Changes.IsEmpty
        );
        if (key == _shownFor)
            return;
        _shownFor = key;
        if (b != null)
            _title.Set(Tr.Get("Effects on {0}"), name);
        else
            _title.Set(Tr.Get("Effects"));
        _against.Set(against[0], against[1..]);
        _effect = other != null && self != null ? Effect.Of(other, self) : Effect.None;
        _show.Button(Differences).Text = Tr.Format("Differences ({0})", _effect.Stats.Count);
        _show.Button(All).Text = Tr.Format("All {0}", self?.Stats.Count ?? 0);
        _stateKey =
            other == null || self == null ? (b == null && a.Changes.IsEmpty ? "none" : "checking")
            : checking ? "checking"
            : "shown";
        // With an answer already shown, a new check fades it rather than adding a line that moves the list.
        var stale = checking && other != null && self != null;
        var state = _stateKey switch
        {
            "none" => Tr.Get("No changes yet"),
            "checking" when !stale => Tr.Get("Checking…"),
            _ => _effect.IsEmpty ? Tr.Get("No difference.") : "",
        };
        SideLine.OnLabel(_state, state, TextStyle.Small);
        _state.TooltipText = "";
        _state.Visible = state.Length > 0;
        _rows.Modulate = stale ? Colors.White with { A = 0.5f } : Colors.White;
        Rows(name);
    }

    private void Rows(string name)
    {
        Ui.Clear(_rows);
        var n = 0;
        foreach (var stat in _effect.Stats)
        {
            n++;
            var row = Ui.Add(_rows, Look.Panel(Art.RowBox, "Cs2EffectRow" + n.ToString(CultureInfo.InvariantCulture)));
            var line = Ui.Add(row, Ui.Row(8));
            var label = Ui.Clipped("", TextStyle.Small);
            Ui.SetClipped(label, FullStatsView.Name(stat));
            line.AddChild(label);
            line.AddChild(Ui.Label((stat.A?.Text ?? "—") + " → " + (stat.B?.Text ?? "—"), TextStyle.Small));
            var delta = Ui.Label(FullStatsView.Delta(stat), TextStyle.Small);
            delta.CustomMinimumSize = new Vector2(64, 0);
            delta.HorizontalAlignment = HorizontalAlignment.Right;
            if (stat.BBetter is { } better)
                delta.LabelSettings = Compare.CompareText.Coloured(better ? Look.Safe : Look.Deadly, TextStyle.Small);
            line.AddChild(delta);
        }
        if (_effect.Modifiers.Count == 0)
            return;
        var head = Ui.Add(_rows, Ui.Row(8));
        var title = Ui.Label(Tr.Get("Modifiers"), TextStyle.Label);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        var counts = new SideLine(TextStyle.Tiny);
        var (added, removed) = (_effect.Added.ToString(Tr.Culture), _effect.Removed.ToString(Tr.Culture));
        var alone = PanelState.B is not { CanChange: true };
        if (_effect.Added > 0 && _effect.Removed > 0)
            counts.Set(
                alone ? Tr.Get("{0} added · {1} gone") : Tr.Get("{0} added on {2} · {1} gone on {2}"),
                added,
                removed,
                name
            );
        else if (_effect.Added > 0)
            counts.Set(alone ? Tr.Get("{0} added") : Tr.Get("{0} added on {1}"), added, name);
        else if (_effect.Removed > 0)
            counts.Set(alone ? Tr.Get("{0} gone") : Tr.Get("{0} gone on {1}"), removed, name);
        head.AddChild(counts.Root);
        var m = 0;
        foreach (var (modifier, count) in _effect.Merged)
        {
            m++;
            var row = Ui.Add(_rows, Look.Panel(Art.RowBox, "Cs2EffectMod" + m.ToString(CultureInfo.InvariantCulture)));
            var line = Ui.Add(row, Ui.Row(6));
            var (glyph, colour) = modifier.Line.Kind switch
            {
                ModifierSign.Positive => ("▲", Look.Safe),
                ModifierSign.Negative => ("▼", Look.Deadly),
                _ => ("▶", Look.Risky),
            };
            var mark = Ui.Label(glyph, TextStyle.Small);
            mark.Modulate = colour;
            line.AddChild(mark);
            var text = Ui.Clipped("", TextStyle.Small);
            Ui.SetClipped(text, FullStatsView.LineText(modifier.Line));
            line.AddChild(text);
            // A technique's bonus is one copy per obstacle it applies on, each only on its own: not stacked.
            if (count > 1)
                line.AddChild(
                    Ui.Label(
                        modifier.Line.Group == ModifierGroup.Agility
                            ? Tr.Plural("on {0} obstacle", "on {0} obstacles", count, count)
                            : Tr.Format("×{0}", count),
                        TextStyle.Tiny
                    )
                );
            // Which side has it: the line is the side's own, or gone from it.
            line.AddChild(Ui.Label(modifier.InB ? Tr.Format("{0} only", name) : Tr.Get("gone"), TextStyle.Tiny));
        }
    }

    /// <summary>Whether the panel is checking, shows an effect or has none: its state as <c>cs2 ui</c> names it.</summary>
    public string StateKey => _stateKey;

    /// <summary>The panel for <c>cs2 ui</c>: its state and counts, then a line per row.</summary>
    public void Describe(StringBuilder text)
    {
        text.Append("\neffect state=")
            .Append(_stateKey)
            .Append(" diffs=")
            .Append(_effect.Stats.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" modifiers=")
            .Append(_effect.Modifiers.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var stat in _effect.Stats)
            text.Append("\neffect_row ")
                .Append(stat.Key)
                .Append(' ')
                .Append(stat.A?.Text ?? "—")
                .Append(' ')
                .Append(stat.B?.Text ?? "—")
                .Append(' ')
                .Append(Effect.Difference(stat) ?? "—");
    }
}
