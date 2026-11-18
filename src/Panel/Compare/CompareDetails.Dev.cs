using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Compare;

internal sealed partial class CompareDetails
{
    /// <summary>Lists the nodes wider at minimum than <paramref name="limit"/>, widest first, for the bridge.</summary>
    public string Widths(float limit) => Results.Details.WidthsUnder(_root, limit);

    /// <summary>
    /// Scrolls so the node named <paramref name="name"/> is in view (<c>top</c>, <c>end</c>), for the bridge.
    /// </summary>
    public string Reveal(string name)
    {
        if (name == "top" || name == "end")
        {
            _scroll.ScrollVertical = name == "top" ? 0 : (int)_scroll.GetVScrollBar().MaxValue;
            return "scrolled to the " + name;
        }
        if (_body.FindChild(name, recursive: true, owned: false) is not Control control)
            return "no node " + name + " in the comparison's details";
        // Its tab first, so it has a place to scroll to.
        if (_tabs.TabOf(control) is { } tab)
            _tabs.Choose(tab, save: false);
        var top = 0f;
        for (Node? node = control; node is Control c && node != _scroll; node = node.GetParent())
            top += c.Position.Y;
        _scroll.ScrollVertical = (int)top;
        return "revealed " + name;
    }
}
