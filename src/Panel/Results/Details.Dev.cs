using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Results;

internal sealed partial class Details
{
    /// <summary>
    /// Scrolls the details so the node named <paramref name="name"/> is in view (<c>end</c>: the bottom, <c>top</c>:
    /// the top), for the bridge. Returns what it did.
    /// </summary>
    public string Reveal(string name)
    {
        if (name == "top" || name == "end")
        {
            _scroll.ScrollVertical = name == "top" ? 0 : (int)_scroll.GetVScrollBar().MaxValue;
            return "scrolled to the " + name;
        }
        if (_body.FindChild(name, recursive: true, owned: false) is not Control control)
            return "no node " + name + " in the details";
        _scroll.EnsureControlVisible(control);
        return "revealed " + name;
    }

    /// <summary>Lists the nodes wider at minimum than <paramref name="limit"/>, widest first, for the bridge.</summary>
    public string Widths(float limit) => WidthsUnder(_root, limit);

    public static string WidthsUnder(Control root, float limit)
    {
        var found = new List<(float Width, string Path)>();
        void Walk(Node node, string path)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is not Control { Visible: true } control)
                    continue;
                var at = path + "/" + control.Name + ":" + control.GetType().Name;
                var width = control.GetCombinedMinimumSize().X;
                if (width > limit)
                    found.Add((width, at));
                Walk(control, at);
            }
        }
        Walk(root, root.Name);
        return string.Join(
            "\n",
            found
                .OrderByDescending(f => f.Width)
                .Take(25)
                .Select(f => f.Width.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " " + f.Path)
        );
    }
}
