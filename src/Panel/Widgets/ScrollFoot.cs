using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// Keeps a list scrolled to its foot there when its rows change: a target returned or ignored while the player reads
/// the end of the list would otherwise leave them partway up it.
/// </summary>
internal static class ScrollFoot
{
    public static void Hold(ScrollContainer scroll)
    {
        var bar = scroll.GetVScrollBar();
        var atFoot = false;
        // Only a list that scrolls has a foot to stay at; a short one growing as results arrive stays at its top.
        bool AtFoot() => bar.MaxValue > bar.Page + 1 && bar.Value + bar.Page >= bar.MaxValue - 1;
        bar.ValueChanged += _ => atFoot = AtFoot();
        bar.Changed += () =>
        {
            if (atFoot && !AtFoot())
                bar.Value = bar.MaxValue - bar.Page;
            atFoot = AtFoot();
        };
    }
}
