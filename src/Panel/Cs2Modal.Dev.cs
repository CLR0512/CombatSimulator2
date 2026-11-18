using Godot;

namespace MelvorMods.CombatSimulator2.Panel;

// What a bench reads of the window: its root, to find the lists to scroll, and whether it has finished changing.
internal static partial class Cs2Modal
{
    public static SetupTab.SetupTabView? SetupTabBuilt => _setupView;

    /// <summary>The scale the window is drawn at now, read from the window itself.</summary>
    public static float DrawnNow => Root?.Scale.X ?? WindowScale.DrawnScale;

    /// <summary>The room the window was last laid out in on screen, in the canvas's units.</summary>
    public static Vector2 Room => _modal?.Room ?? Vector2.Zero;

    public static Control? Root => _root is { } root && GodotObject.IsInstanceValid(root) ? root : null;

    /// <summary>Closed, or open with the view shown built and nothing left to build: what a player can use.</summary>
    public static bool Settled =>
        !IsOpen
        || Root != null
            && _steps.Count == 0
            && !ModalTiming.Measuring
            && (
                PanelState.Prefs.View == Settings ? _settings != null
                : PanelState.Prefs.View == Setup ? _setupView != null
                : PanelState.Prefs.View == Stats ? _statsView != null
                : _simulate is { Building: false }
            );
}
