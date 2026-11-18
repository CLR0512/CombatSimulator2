using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Help;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// Says results may be wrong: CS2 runs on a game build it wasn't checked with, or the results shown were made on one.
/// In the results' head, above the list and the chart and never over them. ✕ folds it to an icon at the end of the
/// toolbar until the window next opens; nothing turns it off for good.
/// </summary>
internal sealed class UncheckedStrip
{
    private static bool _folded;
    private static string _state = "hidden";

    private readonly PanelContainer _strip;
    private readonly Label _text;
    private readonly Button _icon;
    private bool _wanted;

    /// <summary>Builds the strip into <paramref name="head"/> and its folded icon at the end of <paramref name="toolbar"/>.</summary>
    public UncheckedStrip(Container head, Control toolbar)
    {
        _strip = Ui.Add(head, new PanelContainer { Name = "Cs2Unchecked" });
        _strip.AddThemeStyleboxOverride(
            "panel",
            new StyleBoxFlat
            {
                BgColor = Look.RiskyFill,
                BorderColor = Look.Risky,
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                ContentMarginLeft = 12,
                ContentMarginRight = 6,
                ContentMarginTop = 4,
                ContentMarginBottom = 4,
            }
        );
        var row = Ui.Add(_strip, Ui.Row(10));
        var mark = Ui.Picture(AboutIcons.Get(AboutIcons.Unchecked), 16);
        mark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(mark);
        // Light text on the coloured box, as the other alerts have.
        _text = Ui.Add(row, Ui.Label("", TextStyle.Label, wrap: true));
        _text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _text.MouseFilter = Control.MouseFilterEnum.Stop;
        var fold = Ui.Add(row, Ui.Button("✕", "", "Cs2UncheckedFold", Fold));
        fold.Flat = true;
        fold.TooltipText = Tr.Get("Fold until CS2 next opens");

        _icon = Ui.Button("", "", "Cs2UncheckedFolded", ShowAgain);
        _icon.Flat = true;
        _icon.Icon = AboutIcons.Get(AboutIcons.Unchecked);
        // The texture is drawn large to stay sharp at every scale: held to the toolbar's text size, so the row keeps
        // its height.
        _icon.ExpandIcon = true;
        _icon.AddThemeConstantOverride("icon_max_width", 16);
        _icon.CustomMinimumSize = new Vector2(24, 24);
        _icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _icon.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        if (toolbar is Container bar)
            bar.AddChild(_icon);
        Show(false);
    }

    /// <summary>The strip as last shown, for <c>cs2 ui</c>: <c>unchecked=shown|folded|hidden</c>.</summary>
    public static string Describe() => "unchecked=" + _state;

    /// <summary>Shows the strip on the next open, however it was left.</summary>
    public static void Unfold() => _folded = false;

    /// <summary>
    /// Shows the strip when CS2 isn't checked with this game build, or when <paramref name="shownUnchecked"/>: the
    /// results on screen were made on a build it wasn't checked with.
    /// </summary>
    public void Show(bool shownUnchecked)
    {
        var state = LiveGame.Checked;
        _wanted = state.Shows || shownUnchecked;
        if (state.Shows)
        {
            _text.Text = Tr.Get("Not checked with this game version. Results may be wrong.");
            var listed = Usage.Manifest is { } manifest
                ? string.Join(", ", manifest.GameBuilds.Select(b => b.Version))
                : "?";
            // Translators: {0} is CS2's version, {1} the game versions it was checked with, {2} this game's version
            // and {3} its build number.
            _text.TooltipText = Tr.Format(
                "CS2 {0} was checked with Melvor Idle 2 {1}. This is {2} (build {3}).",
                [Usage.Version, listed, state.Version ?? "?", state.Build]
            );
        }
        else
        {
            _text.Text = Tr.Get("Made on a game version CS2 wasn't checked with. Results may be wrong.");
            _text.TooltipText = "";
        }
        _icon.TooltipText = _text.Text;
        Apply();
    }

    private void Fold()
    {
        _folded = true;
        Apply();
    }

    private void ShowAgain()
    {
        _folded = false;
        Apply();
    }

    private void Apply()
    {
        _strip.Visible = _wanted && !_folded;
        _icon.Visible = _wanted && _folded;
        _state =
            !_wanted ? "hidden"
            : _folded ? "folded"
            : "shown";
    }
}
