using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The Setup column on the left of Simulate: side A's card, side B's when comparing, the key stats and "+ Compare…".
/// It asks for each side's check when the side changes and shows the cards from the answers.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class SetupColumn
{
    private const float Width = 300;
    private const int SwapSize = 24;
    private const int SwapTrim = 7;

    private readonly MarginContainer _root;
    private readonly VBoxContainer _column;
    private readonly SetupCard _cardA = new(SideLetter.A);
    private readonly SetupCard _cardB = new(SideLetter.B);
    private readonly KeyStats _stats = new();
    private readonly Control _comparing;
    private readonly Button _compare;
    private readonly Button _swap;
    private readonly Label _problem;

    public SetupColumn()
    {
        _column = Ui.Column(8);
        _column.CustomMinimumSize = new Vector2(Width, 0);
        // A margin on the window's side only, as the column has no panel: on the other the row's own gap leaves the
        // chart's box as far from the column as from the details panel's box.
        // Two cards and the key stats outgrow the smallest room: the column scrolls on its own then, so the filters,
        // the chart and the run control stay in place.
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(_column);
        _root = Ui.Margin(ScrollFade.Wrap(scroll), 12, 0, 0, 0);
        _root.Name = "Cs2SetupColumn";
        _column.AddChild(_cardA.Root);
        // Between the two cards: "vs" at the left, Swap halfway across as a bare sign, quiet until pointed at, and
        // at the right what takes side B away, ending where the cards' own buttons end.
        _comparing = Ui.Add(_column, new MarginContainer { Name = "Cs2Comparing" });
        var versus = Ui.Add(_comparing, Ui.Label(Tr.Get("vs"), TextStyle.Small));
        versus.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        versus.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _swap = Ui.Button("⇅", "", "Cs2Swap", SideSwap.Swap);
        _swap.AddThemeFontSizeOverride("font_size", SwapSize);
        foreach (var box in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            _swap.AddThemeStyleboxOverride(box, new StyleBoxEmpty());
        foreach (
            var (state, alpha) in new[]
            {
                ("font_color", 0.65f),
                ("font_focus_color", 0.65f),
                ("font_hover_color", 1f),
                ("font_pressed_color", 0.4f),
                ("font_hover_pressed_color", 0.4f),
                ("font_disabled_color", 0.25f),
            }
        )
            _swap.AddThemeColorOverride(state, Colors.White with { A = alpha });
        // The sign's line is taller than the sign: given back, so the row keeps the cards close.
        var sign = Ui.Add(_comparing, Ui.Margin(_swap, 0, -SwapTrim, 0, -SwapTrim));
        sign.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        sign.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var stop = Ui.Button(Tr.Get("✕ Clear"), Look.Button4, "Cs2StopCompare", () => PanelState.SetCompareWith(null));
        stop.TooltipText = Tr.Format("Clear comparison: back to side {0} alone", SideLine.A);
        SetupCard.Compact(stop);
        var ending = Ui.Add(_comparing, Ui.Margin(stop, 0, 0, SetupCard.RightInset, 0));
        ending.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _column.AddChild(_cardB.Root);
        _problem = Ui.Add(_column, Ui.Label("", TextStyle.Tiny, "Cs2SetupProblem", wrap: true));
        _problem.Modulate = Look.Risky;
        _column.AddChild(_stats.Root);
        _compare = Ui.Button(Tr.Get("+ Compare…"), Look.Button8, "Cs2Compare", () => ComparePressed?.Invoke());
        _compare.TooltipText = Tr.Get("Compare this setup with changes, a loadout or a saved run");
        _column.AddChild(_compare);
        foreach (var card in new[] { _cardA, _cardB })
        {
            card.ChangePressed += letter => ChangePressed?.Invoke(letter);
            card.EditPressed += letter => EditPressed?.Invoke(letter);
            card.SlotPressed += (letter, slot) => SlotPressed?.Invoke(letter, slot);
            card.SlotStepped += (letter, slot) => SlotStepped?.Invoke(letter, slot);
            card.RequirementsChosen += (letter, ignore) =>
            {
                if ((letter == SideLetter.A ? PanelState.A : PanelState.B) is { } side)
                    PanelState.SetSide(letter, side.WithIgnoreRequirements(ignore));
            };
        }
        _cardA.VariantPressed += () => PanelState.SetSide(SideLetter.B, PanelState.A);
        _stats.AllPressed += () => StatsPressed?.Invoke();
    }

    /// <summary>Raised when a slot of a card's gear grid is clicked.</summary>
    public event Action<SideLetter, string>? SlotPressed;

    /// <summary>Raised when a slot of a card's gear grid is right-clicked.</summary>
    public event Action<SideLetter, string>? SlotStepped;

    /// <summary>The Change button on <paramref name="letter"/>'s card.</summary>
    public Control ChangeButton(SideLetter letter) => (letter == SideLetter.A ? _cardA : _cardB).ChangeButton;

    /// <summary>The control a card draws for <paramref name="slot"/>; null when it isn't drawn.</summary>
    public Control? Slot(SideLetter letter, string slot) => (letter == SideLetter.A ? _cardA : _cardB).Slot(slot);

    public Control Root => _root;

    public event Action<SideLetter>? ChangePressed;

    public event Action? StatsPressed;

    public event Action<SideLetter>? EditPressed;

    public event Action? ComparePressed;

    /// <summary>
    /// Why the sides can't be run as they are (a loadout that is gone, gear or a prayer a side can't use); null when
    /// they can.
    /// </summary>
    public string? Problem { get; private set; }

    /// <summary>Shows both sides as the panel state has them; <paramref name="locked"/> while a run goes.</summary>
    public void Refresh(bool locked)
    {
        var game = LiveGame.Find();
        var character = game != null ? LiveGame.CharacterName(game) : "";
        var loadouts = game != null ? LiveGame.Loadouts(game) : [];
        var a = PanelState.A;
        var b = PanelState.B;
        Problem = null;
        string? Name(SideState side)
        {
            if (side.Base.Kind != SideBaseKind.Loadout)
                return null;
            var loadout = loadouts.FirstOrDefault(l => l.Slot == side.Base.Id && l.Unlocked);
            if (loadout == null)
                Problem = Tr.Format(
                    "Loadout {0} is gone or locked: pick another with Change.",
                    Ids.Brief(side.Base.Id!)
                );
            return loadout?.Name;
        }
        var saved = b is { CanChange: false } ? RunStore.Shared.List().FirstOrDefault(h => h.Id == b.Base.Id) : null;
        if (b is { CanChange: false } && saved == null)
            Problem = Tr.Get("The saved run is gone: pick another with Change.");
        // Something on a side it can't use, kept on by a lowered level: it runs once that comes off.
        Problem ??=
            SetupTab.Unwearable.Problem(a, PanelState.CheckA, b != null ? "A" : null)
            ?? (b is { CanChange: true } ? SetupTab.Unwearable.Problem(b, PanelState.CheckB, "B") : null);
        // A side being checked again keeps its last answer on its card, faded, until the new one is in.
        _cardA.Show(
            a,
            PanelState.CheckA ?? SetupTab.SideChecker.Latest(SideLetter.A, a),
            PanelState.CheckA == null,
            character,
            Name(a),
            b != null,
            locked,
            true,
            null
        );
        _cardB.Root.Visible = b != null;
        _comparing.Visible = b != null;
        _compare.Visible = b == null;
        _compare.Disabled = locked;
        // A saved run has results only, so it can't be the side others are measured against.
        _swap.Disabled = locked || b is { CanChange: false };
        _swap.TooltipText = b is { CanChange: false }
            ? Tr.Get("A saved run can only be side B")
            : Tr.Format("Swap sides {0} and {1}; results shown swap too", SideLine.A, SideLine.B);
        if (b != null)
            // Side B is edited from its card too, whatever it starts from; a saved run has nothing to edit.
            _cardB.Show(
                b,
                PanelState.CheckB ?? SetupTab.SideChecker.Latest(SideLetter.B, b),
                PanelState.CheckB == null,
                character,
                Name(b),
                true,
                locked,
                b.CanChange,
                saved
            );
        _problem.Visible = Problem != null;
        _problem.Text = Problem ?? "";
        // Two sides: A beside B. One side with changes: the character without them beside it with them, so an
        // edit shows what it does. Else the side alone.
        var sheetA = (PanelState.CheckA ?? SetupTab.SideChecker.Latest(SideLetter.A, a))?.Sheet;
        var stance = PanelState.CheckA?.Changes.After.Stance;
        if (b != null)
            _stats.Show(sheetA, b.CanChange ? PanelState.CheckB?.Sheet : null, stance);
        else if (SetupTab.SideChecker.BaseOfA?.Sheet is { } unchanged)
            _stats.Show(unchanged, sheetA, stance, ownChanges: true);
        else
            _stats.Show(sheetA, null, stance);
    }

    /// <summary>The sides' gear as <c>cs2 ui</c> prints it.</summary>
    public void Describe(System.Text.StringBuilder text)
    {
        text.Append("\ncard_a state=").Append(_cardA.State).Append(" gear=").Append(_cardA.GearLine);
        if (PanelState.B != null)
            text.Append("\ncard_b state=").Append(_cardB.State).Append(" gear=").Append(_cardB.GearLine);
        text.Append("\nkey_stats ").Append(_stats.Shown);
        if (SetupTab.SideChecker.SidesSame())
            text.Append("\nsides=same");
        if (Problem != null)
            text.Append("\nsetup_problem=").Append(Problem);
    }
}
