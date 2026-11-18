using System.Text;
using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// What the game refused of a side's changes, shown for a few seconds at the top of the window (or its foot, a
/// setting), on whichever tab is open: the change and the game's reason. A refused change isn't kept, so this is where its reason is read. It goes
/// with the player's next change.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class RefusalNotice
{
    private const long ShownMs = 6000;
    private const long FadeMs = 600;

    private readonly Control _owner;
    private readonly PanelContainer _panel;
    private readonly VBoxContainer _lines;
    private long _shownAt = -1;

    public RefusalNotice(Control owner)
    {
        _owner = owner;
        _panel = Look.Panel(Art.TooltipDarkBox, "Cs2Refused");
        _panel.TopLevel = true;
        _panel.Visible = false;
        _panel.ZIndex = 9;
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _lines = Ui.Add(_panel, Ui.Column(2));
        owner.AddChild(_panel);
    }

    /// <summary>Once a frame while the window is open: shows a new notice, and fades the one shown out in time.</summary>
    public void OnFrame()
    {
        var at = PanelState.RefusedAt;
        if (at < 0)
        {
            _panel.Visible = false;
            _shownAt = -1;
            return;
        }
        var age = System.Environment.TickCount64 - at;
        if (age >= ShownMs)
        {
            PanelState.ClearRefused();
            return;
        }
        if (at != _shownAt)
        {
            _shownAt = at;
            Ui.Clear(_lines);
            var names = PanelState.Names();
            foreach (var (_, outcome) in PanelState.Refused)
            {
                var title = Ui.Label(
                    Tr.Format("Refused: {0}", Tr.Note(ChangeNotes.Describe(outcome.Change, outcome.From, names))),
                    TextStyle.BodyStrong
                );
                title.LabelSettings = Compare.CompareText.Coloured(Look.Deadly, TextStyle.BodyStrong);
                _lines.AddChild(title);
                _lines.AddChild(Ui.Label(Tr.Notes(outcome.Reasons, "; "), TextStyle.Small));
            }
            _panel.Visible = true;
        }
        // A top-level panel skips its owner's transform: it takes the window's size factor itself.
        var scale = _owner.GetGlobalTransform().Scale;
        _panel.Scale = scale;
        _panel.ResetSize();
        var size = _panel.GetCombinedMinimumSize() * scale;
        var bounds = _owner.GetGlobalRect();
        // Under the window's title bar, or above its foot: the player's choice in Settings.
        _panel.GlobalPosition = new Vector2(
            bounds.Position.X + (bounds.Size.X - size.X) / 2,
            PanelState.Prefs.NoticesAtBottom ? bounds.End.Y - size.Y - 24 * scale.Y : bounds.Position.Y + 64 * scale.Y
        ).Round();
        _panel.Modulate = Colors.White with { A = Math.Clamp((ShownMs - age) / (float)FadeMs, 0f, 1f) };
    }

    /// <summary>The notice for <c>cs2 ui</c>: <c>refused a|b &lt;short form&gt; &lt;reason&gt;</c> per change refused.</summary>
    public static void Describe(StringBuilder text)
    {
        foreach (var (letter, outcome) in PanelState.Refused)
            text.Append("\nrefused ")
                .Append(letter == SideLetter.A ? 'a' : 'b')
                .Append(' ')
                .Append(outcome.Change.ShortForm)
                .Append(' ')
                .Append(outcome.Reasons.English("; "));
    }
}
