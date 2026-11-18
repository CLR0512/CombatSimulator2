using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A line of text that names a side: the side's letter is drawn in gold, as on the side's card, wherever it stands
/// in the line. The text marks its letters with <see cref="A"/> and <see cref="B"/>; the line is rebuilt only when
/// its text changes, so it costs nothing while it stands.
/// </summary>
internal sealed class SideLine
{
    private const char Mark = '\u0001';

    /// <summary>Side A's letter, marked to be drawn in gold by a <see cref="SideLine"/>.</summary>
    public const string A = "\u0001A";

    /// <summary>Side B's letter, marked to be drawn in gold by a <see cref="SideLine"/>.</summary>
    public const string B = "\u0001B";

    private readonly TextStyle _style;
    private readonly bool _wrap;

    // A line over a button draws in the button's own font and size, not a text style's.
    private LabelSettings? _plain;
    private LabelSettings? _gold;
    private string? _shown;

    /// <summary>A line in <paramref name="style"/>; with <paramref name="wrap"/> it breaks between words.</summary>
    public SideLine(TextStyle style, string name = "", bool wrap = false)
    {
        _style = style;
        _wrap = wrap;
        if (wrap)
        {
            var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            if (name.Length > 0)
                flow.Name = name;
            flow.AddThemeConstantOverride("h_separation", 0);
            flow.AddThemeConstantOverride("v_separation", 0);
            Root = flow;
        }
        else
            Root = Ui.Row(0, name);
    }

    public Container Root { get; }

    /// <summary>The marked letter of a side given as "A" or "B".</summary>
    public static string Of(string letter) => letter == "A" ? A : B;

    /// <summary>Marked text as plain text, for a tooltip or a text listing.</summary>
    public static string Plain(string text) => Model.Marks.Plain(text);

    /// <summary>The gold a side's letter is drawn in, for a label that is nothing but the letter.</summary>
    public static LabelSettings Gold(TextStyle style) => Compare.CompareText.Coloured(Look.Best, style);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, SideLine> _onButtons = [];
    private static readonly string[] _fontColours =
    [
        "font_color",
        "font_hover_color",
        "font_pressed_color",
        "font_hover_pressed_color",
        "font_focus_color",
        "font_disabled_color",
    ];

    /// <summary>
    /// Gives <paramref name="button"/> marked text: the button keeps the plain text for its size, drawn see-through,
    /// and the line is laid over its middle.
    /// </summary>
    public static void OnButton(Button button, string text)
    {
        button.Text = Plain(text);
        if (!_onButtons.TryGetValue(button, out var line))
        {
            line = new SideLine(TextStyle.Body);
            line._plain = (LabelSettings)Look.Style(TextStyle.Body).Duplicate();
            Look.MarkSkipped(line._plain);
            line._plain.Font = button.GetThemeFont("font");
            line._plain.FontSize = button.GetThemeFontSize("font_size");
            line._plain.FontColor = button.GetThemeColor("font_color");
            line._gold = (LabelSettings)line._plain.Duplicate();
            Look.MarkSkipped(line._gold);
            line._gold.FontColor = Look.Best;
            _onButtons.Add(button, line);
            foreach (var colour in _fontColours)
                button.AddThemeColorOverride(colour, Colors.Transparent);
            var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            centre.AddChild(line.Root);
            button.AddChild(centre);
        }
        line.Set(text);
        line.Root.Modulate = Colors.White with { A = button.Disabled ? 0.5f : 1f };
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Label, SideLine> _onLabels = [];

    /// <summary>
    /// Gives a clipped <paramref name="label"/> marked text: the label keeps the plain text for its size, tooltip and
    /// place, drawn see-through, and the line is laid over it, cut where the label ends.
    /// </summary>
    public static void OnLabel(Label label, string text, TextStyle style, Color? tint = null)
    {
        Ui.SetClipped(label, Plain(text));
        label.TooltipText = text;
        if (!_onLabels.TryGetValue(label, out var line))
        {
            line = new SideLine(style);
            _onLabels.Add(label, line);
            label.LabelSettings = Compare.CompareText.Coloured(Colors.Transparent, style);
            label.ClipContents = true;
            line.Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            line.Root.MouseFilter = Control.MouseFilterEnum.Ignore;
            ((HBoxContainer)line.Root).Alignment = label.HorizontalAlignment switch
            {
                HorizontalAlignment.Right => BoxContainer.AlignmentMode.End,
                HorizontalAlignment.Center => BoxContainer.AlignmentMode.Center,
                _ => BoxContainer.AlignmentMode.Begin,
            };
            label.AddChild(line.Root);
        }
        line.Set(text);
        line.Root.Modulate = tint ?? Colors.White;
        // The line's pieces come out a little wider than the label's one text: a label that never shortens its text
        // is held as wide as the line, so its end isn't cut.
        if (label.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming)
            label.CustomMinimumSize = new Vector2(line.Root.GetCombinedMinimumSize().X, label.CustomMinimumSize.Y);
        // At some scales the line's pieces round a pixel taller than the label's one text, which clips them: the label
        // is held at least as tall as the line.
        var tall = line.Root.GetCombinedMinimumSize().Y;
        if (tall > label.CustomMinimumSize.Y)
            label.CustomMinimumSize = new Vector2(label.CustomMinimumSize.X, tall);
    }

    /// <summary>A line that never changes, right-aligned in a cell of <paramref name="width"/>.</summary>
    public static Container Cell(string text, TextStyle style, float width)
    {
        var cell = (HBoxContainer)Make(text, style);
        cell.CustomMinimumSize = new Vector2(width, 0);
        cell.Alignment = BoxContainer.AlignmentMode.End;
        return cell;
    }

    /// <summary>A line that never changes.</summary>
    public static Container Make(string text, TextStyle style, bool wrap = false)
    {
        var line = new SideLine(style, wrap: wrap);
        line.Set(text);
        return line.Root;
    }

    /// <summary>
    /// Shows <paramref name="format"/> (already translated) with its <c>{n}</c> filled from <paramref name="args"/>;
    /// an argument that is a side's letter, "A" or "B", is drawn in gold.
    /// </summary>
    public void Set(string format, params string[] args) =>
        Set(
            args.Length == 0
                ? format
                : string.Format(Tr.Culture, format, [.. args.Select(a => a is "A" or "B" ? Of(a) : a)])
        );

    /// <summary>Shows marked text.</summary>
    public void Show(string text) => Set(text);

    private void Set(string text)
    {
        if (text == _shown)
            return;
        _shown = text;
        Ui.Clear(Root);
        var at = 0;
        while (at < text.Length)
        {
            var mark = text.IndexOf(Mark, at);
            if (mark < 0 || mark + 1 >= text.Length)
            {
                AddPlain(text[at..]);
                break;
            }
            AddPlain(text[at..mark]);
            var letter = Ui.Label(text[(mark + 1)..(mark + 2)], _style);
            letter.LabelSettings = _gold ?? Gold(_style);
            Root.AddChild(letter);
            at = mark + 2;
        }
    }

    // A wrapping line holds a label per word, each with the space after it, so it can break between any two.
    private void AddPlain(string text)
    {
        if (text.Length == 0)
            return;
        if (!_wrap)
        {
            var whole = Ui.Label(text, _style);
            if (_plain != null)
                whole.LabelSettings = _plain;
            Root.AddChild(whole);
            return;
        }
        var from = 0;
        while (from < text.Length)
        {
            var space = text.IndexOf(' ', from);
            var to = space < 0 ? text.Length : space + 1;
            Root.AddChild(Ui.Label(text[from..to], _style));
            from = to;
        }
    }
}
