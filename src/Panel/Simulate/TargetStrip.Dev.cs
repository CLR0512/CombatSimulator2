using Godot;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

internal sealed partial class TargetStrip
{
    /// <summary>
    /// Sends the strip the click a player would, for <c>cs2dev strip</c>: at the centre of a target's portrait, of an
    /// area's name (<c>area=</c>) or of its fold mark (<c>fold=</c>; <c>dungeons</c> for the dungeons). Returns what it
    /// hit and the target counts after.
    /// </summary>
    public string Poke(string target, string? area, string? fold)
    {
        Rect2? at = null;
        foreach (var each in _areas)
        {
            var id = each.Id == DungeonsId ? DungeonsId : Ids.Full(each.Id);
            if (fold != null && id == (fold == DungeonsId ? fold : Ids.Full(fold)))
                at = each.FoldRect;
            else if (area != null && id == (area == DungeonsId ? area : Ids.Full(area)))
                at = each.NameRect;
            else
                for (var i = 0; i < each.Portraits.Count; i++)
                    if (target.Length > 0 && each.Targets[i].Key == Ids.FullKey(target))
                        at = each.Portraits[i];
        }
        if (at is not { } rect)
            return "not in the strip: " + (fold ?? area ?? target) + (Open ? "" : " (the strip is closed)");
        Input(
            new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = rect.GetCenter(),
            }
        );
        return "clicked " + (fold ?? area ?? target) + "\n" + StateLines.TargetsLine();
    }
}
