using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// CS2's tooltips at once, as the game's own appear: Godot's built-in ones wait half a second, read once at start-up.
/// The control under the pointer has its tooltip text shown in CS2's hover panel, in the game's tooltip style; its text
/// is lifted while the pointer is on it, so Godot's delayed one never follows, and put back when the pointer leaves. A
/// drawn control that sets its tooltip as the pointer moves over it is marked <see cref="Live"/>: its text is read anew
/// on every move, and an empty one means nothing to show there.
/// </summary>
internal static partial class InstantTooltips
{
    /// <summary>The meta a drawn control carries when it sets its tooltip on each move.</summary>
    public const string Live = "cs2_live_tooltip";

    private static HoverPanel? _panel;
    private static Control? _owner;
    private static Control? _over;

    // The control the game reports under the pointer; the tooltip shown may be one of its ancestors'.
    private static Control? _under;
    private static string? _text;

    // The texts lifted from the controls above the one shown, to give back when the pointer leaves it.
    private static readonly List<(Control Control, string Text)> _lifted = [];

    // The text the panel was last made for.
    private static string? _shown;

    // The scroll of a tooltip too tall to show whole, and the control whose wheel turns it.
    private static ScrollContainer? _scroll;
    private static Control? _hooked;
    private const int WheelStep = 60;

    // A dev build can name the control to show a tooltip for, in place of the one under the pointer.
    static partial void Forced(ref Control? over, ref Vector2 mouse);

    private static Vector2 _mouse = new(float.NaN, float.NaN);

    private const long GapMs = 120;

    // When the panel hides, set while the pointer is on something without a tooltip; 0 when not waiting.
    private static long _hideAt;

    // A loop, not a search by a lambda: a lambda that reads the control under the pointer would make the frame
    // callback allocate an object every frame the window is open.
    private static bool IsLifted(Control control)
    {
        foreach (var lifted in _lifted)
            if (ReferenceEquals(lifted.Control, control))
                return true;
        return false;
    }

    /// <summary>
    /// Once a frame while the window is open: nothing more than reading what is under the pointer unless that or the
    /// pointer changed, so a still panel allocates nothing.
    /// </summary>
    public static void OnFrame(Control owner)
    {
        if (!ReferenceEquals(owner, _owner))
        {
            Forget();
            _owner = owner;
            _panel = new HoverPanel(owner);
        }
        var viewport = owner.GetViewport();
        var mouse = viewport.GetMousePosition();
        var over = viewport.GuiGetHoveredControl();
        Forced(ref over, ref mouse);
        // A redraw that replaces the control under a still pointer leaves the game with nothing hovered until the
        // pointer next moves: it is asked to look again, so the new control's tooltip follows without a gap.
        if (_panel!.Visible && (over == null || !owner.IsAncestorOf(over)))
        {
            viewport.UpdateMouseCursorState();
            over = viewport.GuiGetHoveredControl();
        }
        // Under a still pointer the control can change (a page redrawn after a click puts a new one there), and the
        // control shown can be given its text again (a chart filling in during a run, a row filled again): both
        // are taken too, or Godot's own tooltip would show the new text beside CS2's old one.
        var moved = !ReferenceEquals(over, _under);
        var again = !moved && _over != null && GodotObject.IsInstanceValid(_over) && _over.TooltipText.Length > 0;
        if (mouse == _mouse && !moved && !again && _hideAt == 0)
            return;
        _mouse = mouse;
        _under = over;
        Hook(over);
        // A control without a tooltip of its own shows the nearest one above it, as Godot's tooltips do: a row's,
        // wherever in the row the pointer is. The control now shown has had its text lifted, and still counts.
        while (
            over != null
            && !ReferenceEquals(over, owner)
            && !ReferenceEquals(over, _over)
            && over.TooltipText.Length == 0
            && !IsLifted(over)
            && !over.HasMeta(Live)
            && !_lazy.TryGetValue(over, out _)
        )
            over = over.GetParent() as Control;
        var changed = !ReferenceEquals(over, _over);
        if (changed)
        {
            Leave();
            _over = over;
            // A tooltip that is costly to make is made now, the first time its control is reached.
            if (over != null && _lazy.TryGetValue(over, out var make))
            {
                _lazy.Remove(over);
                over.TooltipText = make();
            }
        }
        if (over == null || !owner.IsAncestorOf(over))
        {
            _panel!.Hide();
            _hideAt = 0;
            return;
        }
        var text = over.TooltipText;
        if (over.HasMeta(Live))
            _text = text.Length > 0 ? text : null;
        else if (text.Length > 0)
            _text = text;
        if (text.Length > 0)
            over.TooltipText = "";
        // Godot's own tooltip looks up the tree when a control has no text, so with this control's lifted it would
        // show its row's beside CS2's: the texts above are lifted as well, for as long as the pointer is here.
        for (
            var up = over.GetParent() as Control;
            up != null && !ReferenceEquals(up, owner);
            up = up.GetParent() as Control
        )
        {
            if (up.TooltipText is not { Length: > 0 } above)
                continue;
            _lifted.RemoveAll(l => ReferenceEquals(l.Control, up));
            _lifted.Add((up, above));
            up.TooltipText = "";
        }
        if (_text == null)
        {
            // A gap between two controls with tooltips (rows of a list): the panel stays a moment, so crossing the
            // gap doesn't blink it. A drawn control saying "nothing here" hides it at once.
            var now = System.Environment.TickCount64;
            if (over.HasMeta(Live) || !_panel!.Visible || (_hideAt != 0 && now >= _hideAt))
            {
                _panel!.Hide();
                _hideAt = 0;
            }
            else if (_hideAt == 0)
                _hideAt = now + GapMs;
            return;
        }
        _hideAt = 0;
        // Made again only for a new text: moving over the same control just moves the panel.
        if (_text != _shown)
        {
            _shown = _text;
            _panel!.Reset();
            _scroll = TooltipBody.Fill(_panel.Content, _text, owner.Size.Y * 0.6f);
        }
        _panel!.ShowAt(mouse, owner.GetGlobalRect());
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Func<string>> _lazy = [];

    /// <summary>
    /// Gives <paramref name="control"/> a tooltip made by <paramref name="make"/> the first time the pointer reaches
    /// it: for text that costs something to make, on controls most of which are never hovered.
    /// </summary>
    public static void Lazy(Control control, Func<string> make) => _lazy.AddOrUpdate(control, make);

    /// <summary>The tooltip shown, its lines joined by " | ", as <c>cs2 ui</c> prints it; empty when none is.</summary>
    public static string Shown =>
        _panel is { Visible: true } && _shown != null
            ? Model.Marks.Plain(_shown).Replace("\n", " | ", StringComparison.Ordinal)
            : "";

    /// <summary>Hides the tooltip and gives its text back, when the window closes.</summary>
    public static void Hide()
    {
        Leave();
        _over = null;
        _under = null;
        Hook(null);
        _mouse = new(float.NaN, float.NaN);
        _panel?.Hide();
    }

    // The wheel turns a tooltip too tall to show whole. It reaches the control under the pointer first, which is
    // where it is taken, before a list around that control would scroll with it.
    private static void Hook(Control? under)
    {
        if (ReferenceEquals(under, _hooked))
            return;
        if (_hooked != null && GodotObject.IsInstanceValid(_hooked))
            _hooked.GuiInput -= Wheel;
        _hooked = under;
        if (under != null)
            under.GuiInput += Wheel;
    }

    private static void Wheel(InputEvent e)
    {
        if (
            _scroll == null
            || !GodotObject.IsInstanceValid(_scroll)
            || _panel is not { Visible: true }
            || e is not InputEventMouseButton { Pressed: true } button
            || button.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown)
        )
            return;
        _scroll.ScrollVertical += button.ButtonIndex == MouseButton.WheelDown ? WheelStep : -WheelStep;
        _hooked?.AcceptEvent();
    }

    // The control the pointer left gets its text back, unless it set a new one meanwhile.
    private static void Leave()
    {
        if (
            _over != null
            && GodotObject.IsInstanceValid(_over)
            && !_over.HasMeta(Live)
            && _text != null
            && _over.TooltipText.Length == 0
        )
            _over.TooltipText = _text;
        _text = null;
        foreach (var (control, text) in _lifted)
            if (GodotObject.IsInstanceValid(control) && control.TooltipText.Length == 0)
                control.TooltipText = text;
        _lifted.Clear();
    }

    private static void Forget()
    {
        Hook(null);
        _scroll = null;
        _panel = null;
        _shown = null;
        _owner = null;
        _over = null;
        _text = null;
    }
}
