using Godot;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// The start form's options folded under the gear and the goal: side A's Ignore requirements, the pool's narrowing,
/// other styles, what else to try, and choices the character doesn't have. Folded, a line under its heading says which are on. Remembered open or
/// folded while the game runs.
/// </summary>
internal sealed class OptionsFold
{
    private static bool _open;
    private readonly Disclosure _fold;
    private readonly Label _on;
    private readonly Func<string> _summary;

    /// <summary>The fold holding <paramref name="options"/>; <paramref name="summary"/> says which are on.</summary>
    public OptionsFold(IEnumerable<Control> options, Func<string> summary)
    {
        _summary = summary;
        _fold = new Disclosure(
            "GearSearchOptions",
            // Translators: Folded section of the gear search's form: what the search tries besides gear, and how.
            Tr.Get("Options"),
            "",
            _open,
            open =>
            {
                _open = open;
                _on!.Visible = !open;
            }
        );
        // Under the heading and wrapping: beside it, the summary would be cut to a few words.
        _on = Ui.Label("", TextStyle.Small, "GearSearchOptionsOn", wrap: true);
        _on.Modulate = Colors.White with { A = 0.7f };
        _on.Visible = !_open;
        _fold.Root.AddChild(_on);
        _fold.Root.MoveChild(_on, 1);
        foreach (var option in options)
            _fold.Body.AddChild(option);
    }

    public VBoxContainer Root => _fold.Root;

    /// <summary>The summary again, after an option was switched.</summary>
    public void Refresh() => _on.Text = _summary();
}
