using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

internal sealed partial class Chart
{
    // The hover's lines in English, in the order shown, for cs2dev chart's reply.
    private readonly List<string> _hoverEnglish = [];

    partial void KeepEnglish(Func<bool, string> text) => _hoverEnglish.Add(text(true));

    partial void ForgetEnglish() => _hoverEnglish.Clear();

    /// <summary>
    /// Sends the chart the mouse event a player would, for <c>cs2dev chart</c>, at the centre of a bar
    /// (<paramref name="action"/> hover, click or shift) or of an area's band (band, hover-band). Returns what it hit.
    /// </summary>
    public string Poke(string action, string id)
    {
        if (action is "fold-all" or "unfold-all")
        {
            PanelState.Prefs.FoldedAreas.Clear();
            if (action == "fold-all")
                PanelState.Prefs.FoldedAreas.AddRange(_groups.Select(g => g.Id));
            PanelState.Update();
            return action + " " + _groups.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " areas";
        }
        if (action == "hover-band")
        {
            var hovered = _bandHits.FirstOrDefault(h => h.Groups.Any(g => g.Id == Ids.Full(id)));
            if (hovered.Groups == null)
                return "no band " + id;
            Input(new InputEventMouseMotion { Position = hovered.Rect.GetCenter() });
            return "hover " + (_hover.Visible ? string.Join(" | ", _hoverEnglish) : "nothing shown");
        }
        if (action == "band")
        {
            var bandHit = _bandHits.FirstOrDefault(h => h.Groups.Any(g => g.Id == Ids.Full(id)));
            if (bandHit.Groups == null)
                return "no band " + id;
            var band = (bandHit.Rect, Group: bandHit.Groups.First(g => g.Id == Ids.Full(id)));
            Input(
                new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    Position = band.Rect.GetCenter(),
                }
            );
            return "band "
                + Ids.Brief(band.Group.Id)
                + (
                    band.Group.AllEmpty
                        ? PanelState.Prefs.OpenedAreas.Contains(band.Group.Id)
                            ? " opened"
                            : " collapsed"
                        : PanelState.Prefs.FoldedAreas.Contains(band.Group.Id)
                            ? " folded"
                            : " unfolded"
                );
        }
        var hit = _barHits.FirstOrDefault(h => h.Bar.Key == Ids.FullKey(id));
        if (hit.Bar == null)
            return "no bar " + id;
        var at = new Vector2(hit.Rect.GetCenter().X, hit.Rect.End.Y - 4);
        if (action == "toggle")
        {
            Input(
                new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    Position = new Vector2(at.X, hit.Rect.End.Y + PortraitRow / 2),
                }
            );
            return "toggle "
                + Ids.BriefKey(hit.Bar.Key)
                + (PanelState.Ignored.Contains(hit.Bar.Key) ? " ignored" : " in")
                + "\n"
                + StateLines.TargetsLine();
        }
        if (action == "hover")
        {
            Input(new InputEventMouseMotion { Position = at });
            return "hover " + (_hover.Visible ? string.Join(" | ", _hoverEnglish) : "nothing shown");
        }
        Input(
            new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = at,
                ShiftPressed = action == "shift",
            }
        );
        return action + " " + Ids.BriefKey(hit.Bar.Key);
    }
}
