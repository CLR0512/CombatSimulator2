using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Dev;

/// <summary>
/// Does to the window what a player does, for a bench: scrolls a named list, and triggers each kind of rebuild and
/// puts the setting back afterwards.
/// </summary>
internal static partial class PanelDrive
{
    public const string Targets = "targets";
    public const string History = "history";
    public const string Modifiers = "modifiers";
    public const string Results = "results";
    public const string Shop = "shop";

    public const string Skin = "skin";
    public const string Language = "language";

    private static string? _skin;
    private static double? _scale;

    /// <summary>
    /// Scrolls <paramref name="list"/> to <paramref name="fraction"/> of its range (0 top, 1 end); false when the
    /// list isn't built or has nothing to scroll.
    /// </summary>
    public static bool ScrollTo(string list, double fraction)
    {
        if (Find(list) is not { } scroll)
            return false;
        var bar = scroll.GetVScrollBar();
        var range = bar.MaxValue - bar.Page;
        if (range <= 0)
            return false;
        scroll.ScrollVertical = (int)Math.Round(Math.Clamp(fraction, 0, 1) * range);
        return true;
    }

    private static ScrollContainer? Find(string list)
    {
        if (Cs2Modal.Root is not { } root)
            return null;
        return list switch
        {
            Targets => root.FindChild("Cs2StripScroll", true, false) as ScrollContainer,
            // The results are the ranked list, or the comparison's list while two runs are compared.
            Results => Shown(root, "Cs2ListScroll") ?? Shown(root, "Cs2CompareScroll"),
            History => root.FindChild("Cs2SettingsScroll", true, false) as ScrollContainer,
            Shop => Shown(root, "Cs2PageEntries"),
            Modifiers => root.FindChild("Cs2ModifiersPanel", true, false)
                ?.FindChildren("*", "ScrollContainer", true, false)
                .OfType<ScrollContainer>()
                .FirstOrDefault(),
            _ => null,
        };
    }

    private static ScrollContainer? Shown(Control root, string name) =>
        root.FindChild(name, true, false) is ScrollContainer scroll && scroll.IsVisibleInTree() ? scroll : null;

    /// <summary>The named list's scroll position and range, for checking a scroll by hand.</summary>
    public static string Describe(string list)
    {
        if (Find(list) is not { } scroll)
            return $"{list}: not built";
        var bar = scroll.GetVScrollBar();
        return $"{list}: at {scroll.ScrollVertical} of {bar.MaxValue - bar.Page:0} ({scroll.GetPath()})";
    }

    /// <summary>
    /// Makes the window rebuild as a change of <paramref name="kind"/> does. The skin alternates between the player's
    /// and the other; <see cref="Restore"/> puts the player's back.
    /// </summary>
    public static void Rebuild(string kind)
    {
        var prefs = PanelState.Prefs;
        switch (kind)
        {
            case Skin:
                _skin ??= prefs.Skin;
                Cs2Modal.SetSkin(prefs.Skin == SkinChoice.MElvUI ? SkinChoice.Base : SkinChoice.MElvUI);
                break;
            case Language:
                // What a language change does to CS2, without changing the game's language.
                Tr.Changed();
                RunCache.Clear();
                RunService.Instance.ClearSheets();
                Cs2Modal.Retext();
                break;
            default:
                throw new ArgumentException($"no rebuild '{kind}'", nameof(kind));
        }
    }

    /// <summary>
    /// Changes the Scale as its buttons do, between 70% and the most the room allows; <see cref="Restore"/> puts the
    /// player's back.
    /// </summary>
    public static void ChangeScale()
    {
        var prefs = PanelState.Prefs;
        _scale ??= prefs.Scale;
        prefs.Scale = WindowScale.Drawn > ScaleSteps.Smallest ? ScaleSteps.Smallest : WindowScale.Cap;
        WindowScale.Apply();
    }

    /// <summary>Puts back every setting <see cref="Rebuild"/> and <see cref="ChangeScale"/> changed.</summary>
    public static void Restore()
    {
        if (_skin is { } skin && PanelState.Prefs.Skin != skin)
            Cs2Modal.SetSkin(skin);
        if (_scale is { } scale)
        {
            PanelState.Prefs.Scale = scale;
            WindowScale.Apply();
        }
        _skin = null;
        _scale = null;
        RestoreState();
    }

    /// <summary>
    /// Presses and releases the left button in the corner of the dimmed room around the window, through the
    /// viewport as a real click goes, so whatever is on top there takes it.
    /// </summary>
    public static string ClickOutside()
    {
        Node? node = Cs2Modal.Root;
        Control? dim = null;
        while (node != null && dim == null)
        {
            dim = node.GetNodeOrNull<Control>("Dim");
            node = node.GetParent();
        }
        if (dim == null || !dim.IsVisibleInTree())
            return "no dimmed room: the window is closed";
        var at = dim.GetGlobalTransformWithCanvas() * new Vector2(2, 2);
        foreach (var pressed in new[] { true, false })
            dim.GetViewport()
                .PushInput(
                    new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.Left,
                        Pressed = pressed,
                        Position = at,
                        GlobalPosition = at,
                    },
                    inLocalCoords: true
                );
        return "clicked outside";
    }
}
