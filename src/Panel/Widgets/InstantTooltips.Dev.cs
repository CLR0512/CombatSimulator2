using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

// A check can't move the player's pointer: it names a control, and the tooltip shows as if the pointer were on it.
internal static partial class InstantTooltips
{
    private static Control? _forced;

    /// <summary>Shows <paramref name="control"/>'s tooltip until called with null.</summary>
    public static void Force(Control? control)
    {
        _forced = control;
        _mouse = new(float.NaN, float.NaN);
    }

    static partial void Forced(ref Control? over, ref Vector2 mouse)
    {
        if (_forced == null || !GodotObject.IsInstanceValid(_forced))
            return;
        over = _forced;
        mouse = _forced.GetGlobalRect().GetCenter();
    }
}
