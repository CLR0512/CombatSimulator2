using Godot;
using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// CS2's dropdown: a bar with the current choice and a down arrow, opening its list under it in CS2's window, drawn as
/// the Areas list is. The game's own dropdown opens a popup window, which it draws unscaled and blurred and as tall as
/// its items; this one is as sharp as the window, no taller than <see cref="MaxHeight"/>, and scrolls past it. Typing
/// while it is open goes to the first choice starting with what was typed, or with its part after a "·".
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed partial class Dropdown
{
    /// <summary>The bar drawn as the targets button is: the game's dark section bar.</summary>
    public const string Section = "section";

    /// <summary>The bar drawn as the filter buttons beside it are (Areas ▾): their button, and a small ▾.</summary>
    public const string Plain = "plain";

    /// <summary>The ▾ alone with no box, for a list that opens from inside a box of its owner's (a name field).</summary>
    public const string Bare = "bare";

    /// <summary>Gold words and a small ▾ with no box, for a choice inside a sentence.</summary>
    public const string Link = "link";

    private const float MaxHeight = 480;
    private const int ArrowSize = 16;
    private const float ArrowGap = 12;
    private const int LinkGap = 6;
    private const ulong TypedFor = 1000;

    private static Texture2D? _arrow;
    private static readonly Dictionary<int, Texture2D> _marks = [];
    private readonly bool _bare,
        _link;
    private Action? _retint;

    // A bare mark points at its list: left while it is shut, down while it is out, and bright while it is out.
    private void ShowBare()
    {
        _bar.Icon = Turned(Open);
        _retint?.Invoke();
    }

    private readonly Button _bar;
    private readonly List<string> _items = [];
    private readonly List<Texture2D?> _icons = [];

    // Why each choice can't be picked now, null when it can: it shows greyed with the reason as its hover.
    private readonly List<string?> _offs = [];
    private readonly List<Button> _rows = [];
    private Control? _catcher;
    private PanelContainer? _panel;
    private ScrollContainer? _scroll;
    private VBoxContainer? _list;
    private string _typed = "";
    private ulong _typedAt;

    public Dropdown(string name, string look)
    {
        (_bare, _link) = (look == Bare, look == Link);
        _bar = Ui.Button("", look is Section or Bare or Link ? "" : Look.Tab, name, Toggle);
        _bar.Alignment = HorizontalAlignment.Left;
        // A bare mark stands in the middle of its bar, clear of the box's edge whichever way it points.
        _bar.IconAlignment = _bare ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        if (look == Section)
        {
            void Apply()
            {
                var box = Look.Box(Art.SectionBox);
                foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
                    _bar.AddThemeStyleboxOverride(state, box);
            }
            // Re-read when the theme changes, so the bar follows the skin.
            Look.OnTheme(_bar, Apply);
            Apply();
            _bar.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        }
        if (look is Bare or Link)
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
                _bar.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        // Set once here: an override raises a theme change, which fits the bar again.
        if (look == Link)
            _bar.AddThemeConstantOverride("h_separation", LinkGap);
        if (look == Link)
            foreach (var state in new[] { "", "_hover", "_pressed", "_hover_pressed", "_focus" })
                _bar.AddThemeColorOverride($"font{state}_color", state == "" ? Look.Best : Look.Best.Lightened(0.2f));
        if (look is Plain or Bare or Link)
        {
            if (look == Plain)
                _bar.ThemeChanged += Edges;
            if (look == Plain)
                _bar.Resized += () =>
                {
                    if ((int)Math.Round(_bar.Size.Y) != _edgedAt)
                        Edges();
                };
            // The ▾ in the text's colour, as the Areas button writes it.
            void Tint()
            {
                foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
                {
                    var colour = _bar.GetThemeColor(state == "normal" ? "font_color" : $"font_{state}_color");
                    // Alone in a field, the mark rests quiet and lights when pointed at or while its list is out.
                    if (look == Bare && state is "normal" or "focus" or "disabled" && !Open)
                        colour = colour with { A = colour.A * 0.45f };
                    _bar.AddThemeColorOverride($"icon_{state}_color", colour);
                }
            }
            Look.OnTheme(_bar, Tint);
            Tint();
            _retint = Tint;
        }
        // Alone in its field, the ▾ is all there is to point at: drawn larger than beside a choice's text.
        _bar.Icon =
            look == Bare ? Turned(false)
            : look is Plain or Link ? Mark(9)
            : Arrow();
        _bar.TreeExiting += Close;
        _bar.ThemeChanged += Fit;
    }

    /// <summary>Holds the bar at <paramref name="width"/>, cutting a longer choice short.</summary>
    public void FixWidth(float width)
    {
        _fixed = width;
        _bar.ClipText = true;
        _bar.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Fit();
    }

    private float _fixed;

    /// <summary>
    /// What the list hangs under, when that isn't the bar alone (a field the bar ends): the list is as wide as it
    /// and starts where it starts, and a choice too long for that is cut short, whole in its tooltip.
    /// </summary>
    public Control? Under { get; set; }

    // As wide as the longest choice with the arrow beside it, so choosing never resizes the bar or its row.
    private void Fit()
    {
        if (_fixed > 0)
        {
            _bar.CustomMinimumSize = new Vector2(_fixed, 0);
            return;
        }
        var (font, size) = (_bar.GetThemeFont("font"), _bar.GetThemeFontSize("font_size"));
        // Words in a sentence fit the choice shown with the ▾ close behind; a bar is as wide as its longest choice.
        var widest =
            _link ? font.GetStringSize(_bar.Text, fontSize: size).X
            : _items.Count == 0 ? 0
            : _items.Max(i => font.GetStringSize(i, fontSize: size).X);
        var gap = (_link ? LinkGap : ArrowGap) + (_bar.Icon?.GetWidth() ?? 0);
        _bar.CustomMinimumSize = new Vector2(widest + gap + _bar.GetThemeStylebox("normal").GetMinimumSize().X, 0);
    }

    public Button Root => _bar;

    /// <summary>Hangs the list from the bar's right edge, for a bar at the right of a narrow panel.</summary>
    public bool RightAligned { get; init; }

    /// <summary>The bar's top and bottom margins when set, for a compact toolbar; the theme's otherwise.</summary>
    public float? Ends { get; init; }

    /// <summary>The box the open list is drawn in, by <see cref="Look.Box"/> name.</summary>
    public string ListBox { get; init; } = Art.TooltipDarkBox;

    /// <summary>Raised with the index of the choice picked.</summary>
    public event Action<int>? ItemSelected;

    public int Selected { get; private set; } = -1;

    public bool Open => _panel is { Visible: true } panel && GodotObject.IsInstanceValid(panel);

    public void Clear()
    {
        _items.Clear();
        _icons.Clear();
        _offs.Clear();
        Selected = -1;
        _bar.Text = "";
    }

    /// <summary>
    /// Adds a choice, drawn with <paramref name="icon"/> before its text in the list when given; with
    /// <paramref name="off"/> it is greyed and can't be picked, and its hover says why.
    /// </summary>
    public void AddItem(string text, Texture2D? icon = null, string? off = null)
    {
        _items.Add(text);
        _icons.Add(icon);
        _offs.Add(off);
        Fit();
    }

    /// <summary>Shows choice <paramref name="index"/> on the bar, without raising <see cref="ItemSelected"/>.</summary>
    public void Select(int index)
    {
        Selected = index >= 0 && index < _items.Count ? index : -1;
        _bar.Text = Selected >= 0 ? _items[Selected] : "";
        Fit();
    }

    public void Close()
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel))
            _panel.Visible = false;
        if (_catcher != null && GodotObject.IsInstanceValid(_catcher))
            _catcher.Visible = false;
    }

    private void Toggle() => (Open ? (Action)Close : Show)();

    private void Show()
    {
        if (_items.Count == 0 || Host() is not { } host)
            return;
        Build(host);
        Overlays.Opened(this, () => Open, Close);
        Ui.Clear(_list!);
        _rows.Clear();
        for (var i = 0; i < _items.Count; i++)
            _rows.Add(Row(i));
        var scale = host.GetGlobalTransform().Scale;
        var room = host.GetGlobalRect();
        _panel!.Scale = scale;
        _catcher!.GlobalPosition = room.Position;
        _catcher.Size = room.Size;
        _catcher.Visible = true;
        _panel.Visible = true;
        var under = Under is { } held && GodotObject.IsInstanceValid(held) ? held.GetGlobalRect() : (Rect2?)null;
        var width = under == null ? Math.Max(_bar.Size.X, _list!.GetCombinedMinimumSize().X + (_link ? 10 : 24)) : 0;
        _scroll!.CustomMinimumSize = new Vector2(width, Math.Min(MaxHeight, _list!.GetCombinedMinimumSize().Y));
        _panel.CustomMinimumSize = new Vector2(under is { } wide ? wide.Size.X / scale.X : 0, 0);
        _panel.ResetSize();
        // Under the bar, or over it when the window has no room below.
        var bar = _bar.GetGlobalRect();
        var height = _panel.Size.Y * scale.Y;
        var y = bar.End.Y + 4 + height <= room.End.Y ? bar.End.Y + 4 : bar.Position.Y - 4 - height;
        // A bare ▾ ends its field, and a right-aligned list keeps inside its panel: either hangs from the right edge.
        var x = under?.Position.X ?? (_bare || RightAligned ? bar.End.X - _panel.Size.X * scale.X : bar.Position.X);
        _panel.GlobalPosition = new Vector2(x, Math.Max(room.Position.Y, y)).Round();
        _typed = "";
        _panel.GrabFocus();
        if (Selected >= 0)
            Callable.From(() => Reveal(Selected)).CallDeferred();
        else
            _scroll.ScrollVertical = 0;
    }

    // CS2's window, which the list is drawn over.
    private Control? Host()
    {
        Control? top = null;
        for (var node = _bar.GetParent(); node != null; node = node.GetParent())
            if (node is Control control)
            {
                top = control;
                if (control.Name == "Cs2Root")
                    break;
            }
        return top;
    }

    // The list and the catcher behind it, made in the window the first time the list opens there.
    private void Build(Control host)
    {
        if (_panel != null && GodotObject.IsInstanceValid(_panel) && _panel.GetParent() == host)
            return;
        // Over the window, under the list: a click anywhere outside the list closes it and goes no further.
        _catcher = new Control
        {
            Name = _bar.Name + "Outside",
            TopLevel = true,
            Visible = false,
            ZIndex = 19,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        var catcher = _catcher;
        catcher.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true })
            {
                Close();
                catcher.AcceptEvent();
            }
        };
        host.AddChild(catcher);
        var panel = Look.Panel(ListBox, _bar.Name + "List");
        panel.TopLevel = true;
        panel.Visible = false;
        if (_bare)
            panel.VisibilityChanged += ShowBare;
        panel.ZIndex = 20;
        panel.FocusMode = Control.FocusModeEnum.All;
        panel.GuiInput += e => Typed(panel, e);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        // A short list from words in a sentence sits snug round its few choices.
        var edge = _link ? 3 : 6;
        panel.AddChild(Ui.Margin(ScrollFade.Wrap(_scroll), edge, edge, edge - 2, edge));
        _list = Ui.Add(_scroll, Ui.Column(2));
        _list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        host.AddChild(panel);
        _panel = panel;
    }

    // A choice: its text, the current one ticked and lit, each lit under the pointer; a click picks it. With an icon,
    // the icon takes the tick's place and the current one's text is gold instead.
    private Button Row(int index)
    {
        var chosen = index == Selected;
        // In a list with icons, a choice without one ("None") gets an empty one, so every text starts in line.
        var icon = _icons[index] ?? (_icons.Any(i => i != null) ? ShrunkIcon.Blank(20) : null);
        var row = Ui.Button(
            (
                icon != null ? ""
                : chosen ? "✓  "
                : "     "
            ) + _items[index],
            "",
            Ui.Id(_bar.Name + "Item", index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            () => Pick(index)
        );
        row.Flat = true;
        row.Alignment = HorizontalAlignment.Left;
        if (Under != null)
        {
            row.ClipText = true;
            row.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            row.TooltipText = _items[index];
        }
        if (_offs[index] is { } off)
            (row.Disabled, row.TooltipText) = (true, off);
        var lit = new StyleBoxFlat { BgColor = Colors.White with { A = 0.08f } };
        lit.SetContentMarginAll(4);
        var plain = new StyleBoxEmpty();
        plain.SetContentMarginAll(4);
        foreach (var state in new[] { "normal", "pressed", "focus", "disabled" })
            row.AddThemeStyleboxOverride(state, chosen ? lit : plain);
        row.AddThemeStyleboxOverride("hover", lit);
        row.AddThemeStyleboxOverride("hover_pressed", lit);
        if (icon != null)
        {
            row.Icon = ShrunkIcon.Of(icon, 20);
            row.AddThemeConstantOverride("h_separation", 8);
            if (chosen)
                foreach (var state in new[] { "", "_hover", "_pressed", "_focus", "_hover_pressed" })
                    row.AddThemeColorOverride($"font{state}_color", Look.Best);
        }
        _list!.AddChild(row);
        return row;
    }

    private void Pick(int index)
    {
        Close();
        if (index == Selected)
            return;
        Select(index);
        ItemSelected?.Invoke(index);
    }

    // Letters typed within a second of each other build one prefix; the list goes to the first choice it starts, or
    // failing that, the first whose part after a "·" it starts ("c" finds "Item · Copper Arrows").
    private void Typed(Control panel, InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key)
            return;
        panel.AcceptEvent();
        if (key.Keycode == Key.Escape)
        {
            Close();
            return;
        }
        if (key.Unicode < 32)
            return;
        var now = Time.GetTicksMsec();
        _typed = (now - _typedAt > TypedFor ? "" : _typed) + char.ConvertFromUtf32((int)key.Unicode);
        _typedAt = now;
        var found = _items.FindIndex(i => Starts(i, _typed));
        if (found < 0)
            found = _items.FindIndex(i => i.Split(" · ").Skip(1).Any(part => Starts(part, _typed)));
        if (found >= 0)
            Reveal(found);
    }

    private static bool Starts(string text, string typed) =>
        text.StartsWith(typed, StringComparison.CurrentCultureIgnoreCase);

    private void Reveal(int index)
    {
        if (_scroll == null || index >= _rows.Count || !GodotObject.IsInstanceValid(_rows[index]))
            return;
        _scroll.ScrollVertical = (int)Math.Max(0, _rows[index].Position.Y - _scroll.Size.Y / 3);
    }

    // A small white ▾, coloured by the button as its text is.
    // The bare mark: a ◂ while its list is shut, a ▾ while it is out. Both are drawn in the middle of one square, so
    // the mark turns about its own centre and stays where it is. Made once each.
    private static Texture2D Turned(bool down)
    {
        if (_turned.TryGetValue(down, out var made) && GodotObject.IsInstanceValid(made))
            return made;
        const int Long = 13;
        const int Short = (Long + 1) / 2;
        const int Inset = (Long - Short) / 2;
        var image = Image.CreateEmpty(Long, Long, false, Image.Format.Rgba8);
        // From the triangle's base to its point, each line two shorter than the last.
        for (var depth = 0; depth < Short; depth++)
        for (var along = depth; along < Long - depth; along++)
        {
            if (down)
                image.SetPixel(along, Inset + depth, Colors.White);
            else
                image.SetPixel(Inset + Short - 1 - depth, along, Colors.White);
        }
        return _turned[down] = ImageTexture.CreateFromImage(image);
    }

    private static readonly Dictionary<bool, Texture2D> _turned = [];

    // A ▾ <paramref name="width"/> wide (odd, so its point is one pixel), made once per width.
    private static Texture2D Mark(int width)
    {
        if (_marks.TryGetValue(width, out var made) && GodotObject.IsInstanceValid(made))
            return made;
        var height = (width + 1) / 2;
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (var y = 0; y < height; y++)
        for (var x = y; x < width - y; x++)
            image.SetPixel(x, y, Colors.White);
        return _marks[width] = ImageTexture.CreateFromImage(image);
    }

    // The section bar's open arrow at the size the targets button draws it: the file is larger, and a button draws its
    // icon as the file is.
    private static Texture2D? Arrow()
    {
        if (_arrow != null && GodotObject.IsInstanceValid(_arrow))
            return _arrow;
        if (Art.File(Art.OpenArrowPath)?.GetImage() is not { } image || image.GetWidth() == 0)
            return null;
        if (image.IsCompressed())
            image.Decompress();
        image.Resize(
            ArrowSize,
            Math.Max(1, ArrowSize * image.GetHeight() / image.GetWidth()),
            Image.Interpolation.Lanczos
        );
        return _arrow = ImageTexture.CreateFromImage(image);
    }
}
