using System.Globalization;
using System.Text;
using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// <c>cs2dev fit</c>: what doesn't fit in the open window, found from its nodes rather than by eye, so every screen
/// size, scale and look can be gone through without judging a screenshot of each.
/// </summary>
internal static class FitCommands
{
    // Less than this past an edge is rounding, not a clip.
    private const float Slack = 1;

    public static Subcommand Sub { get; } =
        new(
            "fit",
            "cs2dev fit | cs2dev fit sizes | cs2dev fit widths [min=100] | cs2dev fit tree <node name> [depth=2]",
            ["min", "depth"],
            [],
            Fit,
            NeedsGame: false,
            Purpose: "What reaches past the window or its clip, labels cut off, scroll bars shown, and the room; sizes: the window's minimum size with each view; widths: what makes the view shown wide; tree: each part's minimum and size under a node"
        );

    private static void Fit(CommandCall call, CommandArgs args)
    {
        var root = Cs2Modal.Root;
        if (root == null || !Cs2Modal.IsOpen)
            throw new CommandException("the window isn't open: cs2 open");
        switch (args.Word(0))
        {
            case null:
                call.Ok(Problems(root));
                return;
            case "sizes":
                Sizes(call);
                return;
            case "tree" when args.Word(1) is { } name:
                var top =
                    root.FindChild(name, true, false) as Control
                    ?? throw new CommandException($"no control {name} in the window");
                call.Ok(Tree(top, args.Int("depth", 2, 1, 8)));
                return;
            case "widths":
                call.Ok(Widths(root, args.Int("min", 100, 1, 4000)));
                return;
            default:
                throw new CommandException(
                    "usage: cs2dev fit | cs2dev fit sizes | cs2dev fit widths [min=100] | cs2dev fit tree <node name> [depth=2]"
                );
        }
    }

    private static string Problems(Control root)
    {
        var lines = new List<string>();
        var window = root.GetGlobalRect();
        var room = Cs2Modal.Room;
        if (window.Size.X > room.X + Slack || window.Size.Y > room.Y + Slack)
            lines.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"clip {root.Name} {Show(window)} outside room {room.X:0}x{room.Y:0}"
                )
            );
        Walk(root, root, window, "window", window, lines);
        // The report note is a window of its own beside this one.
        if (root.GetParent()?.GetParent()?.FindChild("Cs2ReportNote", true, false) is Control { Visible: true } note)
        {
            var noteRect = note.GetGlobalRect();
            Walk(note, note, noteRect, "report", noteRect, lines);
        }
        var text = new StringBuilder();
        foreach (var line in lines)
            text.Append(line).Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"room {room.X:0}x{room.Y:0}\n");
        var problems = lines.Count(l => !l.StartsWith("scroll", StringComparison.Ordinal));
        text.Append(CultureInfo.InvariantCulture, $"fit {problems} problems");
        return text.ToString();
    }

    // Checks each visible control against the clip of its nearest clipping ancestor (the window when there is none)
    // and goes on into its children with its own clip when it has one. A popup drawn on top of its parent answers to
    // the window alone.
    private static void Walk(Node node, Control top, Rect2 clip, string clipName, Rect2 window, List<string> lines)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is CanvasItem { Visible: false })
                continue;
            if (child is not Control control)
            {
                Walk(child, top, clip, clipName, window, lines);
                continue;
            }
            var (allowed, allowedName) = control.TopLevel ? (window, "window") : (clip, clipName);
            var rect = control.GetGlobalRect();
            if (rect.Size.X > 0 && rect.Size.Y > 0 && Outside(rect, allowed))
                lines.Add($"clip {Path(top, control)} {Show(rect)} outside {allowedName} {Show(allowed)}");
            Cut(top, control, lines);
            var (inner, innerName) = (allowed, allowedName);
            if (control is ScrollContainer scroll)
            {
                (inner, innerName) = (Reach(scroll, rect), Path(top, scroll));
                if (scroll.GetHScrollBar().Visible)
                    lines.Add($"scroll {Path(top, scroll)} h");
                if (scroll.GetVScrollBar().Visible)
                    lines.Add($"scroll {Path(top, scroll)} v");
            }
            else if (control.ClipContents)
                (inner, innerName) = (rect, Path(top, control));
            Walk(control, top, inner, innerName, window, lines);
        }
    }

    // What a scroll container shows, grown along each axis it scrolls to the content it can scroll to.
    private static Rect2 Reach(ScrollContainer scroll, Rect2 rect)
    {
        var content = scroll.GetChildren().OfType<Control>().FirstOrDefault(c => c.Visible);
        if (content == null)
            return rect;
        var all = rect.Merge(content.GetGlobalRect());
        var x = scroll.HorizontalScrollMode != ScrollContainer.ScrollMode.Disabled;
        var y = scroll.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled;
        return new Rect2(
            x ? all.Position.X : rect.Position.X,
            y ? all.Position.Y : rect.Position.Y,
            x ? all.Size.X : rect.Size.X,
            y ? all.Size.Y : rect.Size.Y
        );
    }

    // The controls of the view shown that set a minimum width of their own, and the labels that can't wrap, at least
    // min wide: the parts a too-wide view is made of.
    private static string Widths(Control root, int min)
    {
        var text = new StringBuilder();
        var count = 0;
        void Walk(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is CanvasItem { Visible: false })
                    continue;
                if (child is Control control)
                {
                    var own = control.CustomMinimumSize.X;
                    var fixedText =
                        control is Label { AutowrapMode: TextServer.AutowrapMode.Off, ClipText: false } label
                        && label.TextOverrunBehavior == TextServer.OverrunBehavior.NoTrimming;
                    var width = control.GetCombinedMinimumSize().X;
                    if (own >= min || fixedText && width >= min)
                    {
                        count++;
                        text.Append(
                            CultureInfo.InvariantCulture,
                            $"{(own >= min ? "set" : "text")} {width:0} {Path(root, control)}{(control is Label l ? $" \"{l.Text}\"" : "")}\n"
                        );
                    }
                }
                Walk(child);
            }
        }
        Walk(root);
        return text.Append(CultureInfo.InvariantCulture, $"widths {count} at least {min}").ToString();
    }

    // Each visible part under top to depth: its minimum, then its size, both in design units.
    private static string Tree(Control top, int depth)
    {
        var text = new StringBuilder();
        void Walk(Control control, int level)
        {
            var min = control.GetCombinedMinimumSize();
            text.Append(' ', level * 2)
                .Append(
                    CultureInfo.InvariantCulture,
                    $"{_autoNumber.Replace(control.Name, "")} min {min.X:0}x{min.Y:0} size {control.Size.X:0}x{control.Size.Y:0}\n"
                );
            if (level < depth)
                foreach (var child in control.GetChildren().OfType<Control>().Where(c => c.Visible))
                    Walk(child, level + 1);
        }
        Walk(top, 0);
        return text.ToString().TrimEnd();
    }

    private static bool Outside(Rect2 rect, Rect2 allowed) =>
        rect.Position.X < allowed.Position.X - Slack
        || rect.Position.Y < allowed.Position.Y - Slack
        || rect.End.X > allowed.End.X + Slack
        || rect.End.Y > allowed.End.Y + Slack;

    // A label or button that clips or trims its text, with text that doesn't fit: the player sees part of it.
    private static void Cut(Control top, Control control, List<string> lines)
    {
        switch (control)
        {
            case Label label when label.Text.Length > 0 && Trims(label.ClipText, label.TextOverrunBehavior):
                var wide =
                    label.AutowrapMode == TextServer.AutowrapMode.Off
                    && TextWidth(label, label.Text)
                        > label.Size.X - label.GetThemeStylebox("normal").GetMinimumSize().X + Slack;
                if (wide || label.GetVisibleLineCount() < label.GetLineCount())
                    lines.Add($"cut {Path(top, label)} \"{label.Text}\"");
                break;
            case Button button when button.Text.Length > 0 && Trims(button.ClipText, button.TextOverrunBehavior):
                var room = button.Size.X - button.GetThemeStylebox("normal").GetMinimumSize().X + Slack;
                if (button.Icon != null)
                    room -= button.Icon.GetWidth() + button.GetThemeConstant("h_separation");
                if (TextWidth(button, button.Text) > room)
                    lines.Add($"cut {Path(top, button)} \"{button.Text}\"");
                break;
        }
    }

    private static bool Trims(bool clip, TextServer.OverrunBehavior overrun) =>
        clip || overrun != TextServer.OverrunBehavior.NoTrimming;

    private static float TextWidth(Control control, string text) =>
        control
            .GetThemeFont("font")
            .GetMultilineStringSize(text, HorizontalAlignment.Left, -1, control.GetThemeFontSize("font_size"))
            .X;

    // Godot's own names for unnamed nodes carry a number that changes every build; without it, the lines of two
    // screen setups can be compared.
    private static string Path(Control top, Node node) =>
        _autoNumber.Replace(top == node ? top.Name : $"{top.Name}/{top.GetPathTo(node)}", "");

    private static readonly System.Text.RegularExpressions.Regex _autoNumber = new(
        "(?<=/@[A-Za-z0-9]+)@[0-9]+",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    );

    private static string Show(Rect2 r) =>
        string.Create(CultureInfo.InvariantCulture, $"{r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}x{r.Size.Y:0}");

    // The window's minimum with the view shown, as if it didn't scroll: the title bar and tabs over the view's own
    // minimum, in the window's frame. The scroll around the view leaves the view's size out of the window's minimum.
    private static Vector2 WithView(Control root)
    {
        if (
            root.FindChild("Cs2Body", true, false) is not VBoxContainer body
            || body.FindChild("Cs2TopBar", true, false) is not Control bar
            || body.FindChild("Cs2Content", true, false) is not Control content
        )
            return root.GetCombinedMinimumSize();
        var frame = root.GetCombinedMinimumSize() - body.GetCombinedMinimumSize();
        var top = bar.GetCombinedMinimumSize();
        var view = content.GetCombinedMinimumSize();
        return new Vector2(
            Math.Max(top.X, view.X) + frame.X,
            top.Y + body.GetThemeConstant("separation") + view.Y + frame.Y
        );
    }

    private static readonly string[] _views = [Cs2Modal.Simulate, Cs2Modal.Setup, Cs2Modal.Settings, Cs2Modal.Stats];

    // Each view shown in turn, once built and laid out, then the view that was shown before.
    private static void Sizes(CommandCall call)
    {
        var was = PanelState.Prefs.View;
        var text = new StringBuilder();
        var index = 0;
        var frames = 0;
        void Next()
        {
            if (index == _views.Length)
            {
                Cs2Modal.ShowView(was);
                call.Ok(text.Append(CultureInfo.InvariantCulture, $"{_views.Length} views").ToString());
                return;
            }
            Cs2Modal.ShowView(_views[index]);
            frames = 0;
            // Two frames once built: the containers sort their children, then the sizes settle.
            MainThread.When(
                () => Cs2Modal.Settled && ++frames > 2,
                () =>
                {
                    if (Cs2Modal.Root is not { } root)
                    {
                        Cs2Modal.ShowView(was);
                        call.Error("the window closed");
                        return;
                    }
                    var size = WithView(root);
                    text.Append(CultureInfo.InvariantCulture, $"{_views[index]} {size.X:0}x{size.Y:0}\n");
                    index++;
                    Next();
                },
                TimeSpan.FromSeconds(10),
                () =>
                {
                    Cs2Modal.ShowView(was);
                    call.Error($"{_views[index]} never finished building");
                }
            );
        }
        Next();
    }
}
