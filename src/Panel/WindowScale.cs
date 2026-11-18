using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Help;
using MelvorMods.CombatSimulator2.Panel.Widgets;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// How large CS2's window draws: what 100% means, times the player's Scale, held under the cap the room on screen
/// allows. The window follows it at once, by a relayout. Main thread only.
/// </summary>
internal static class WindowScale
{
    private static ModModal? _modal;

    private static float _base = 1.00f;

    /// <summary>
    /// What 100% means: how much larger than its design size the window draws at a Scale of 100%, 0.5–3. Its
    /// design size reads the same size as the game's own text. Setting it applies at once.
    /// </summary>
    public static float Base
    {
        get => _base;
        set
        {
            _base = Math.Clamp(float.IsFinite(value) ? value : 1, 0.5f, 3f);
            if (Room != Vector2.Zero)
                RoomChanged(Room);
            else
                Apply();
        }
    }

    /// <summary>The room the window was last laid out in on screen, in the canvas's units; zero before any open.</summary>
    public static Vector2 Room { get; private set; }

    /// <summary>The largest Scale the room allows; the largest step before the window is first laid out.</summary>
    public static double Cap { get; private set; } = ScaleSteps.Largest;

    /// <summary>The Scale drawn: the saved one, held under the cap.</summary>
    public static double Drawn => ScaleSteps.Drawn(PanelState.Prefs.Scale, Cap);

    /// <summary>The window's scale on screen, which the report note takes too.</summary>
    public static float DrawnScale => (float)ScaleSteps.Crisp(Base * Drawn, Stretch);

    // The pixels the game draws one unit with, from its own stretch of the window.
    private static double Stretch =>
        Godot.Engine.GetMainLoop() is SceneTree { Root: { } root } ? root.GetFinalTransform().X.Length() : 1;

    /// <summary>A scale as a whole percent, for commands and checks.</summary>
    public static string Percent(double scale) =>
        Math.Round(scale * 100).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Raised after the cap or the drawn scale changed, for the Settings row.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Follows <paramref name="modal"/>'s room from now on, and draws it at the scale once the game's tree is ready:
    /// the scale reads the window's stretch from Godot, which a game starting up hasn't made while mods load.
    /// </summary>
    public static void Attach(ModModal modal, ModContext context)
    {
        _modal = modal;
        modal.RoomChanged += RoomChanged;
        context.OnReady(_ => modal.Scale = DrawnScale);
    }

    /// <summary>
    /// Draws the window at the scale now. Popups and the help are closed first when the scale changes, as each kept
    /// the scale it opened at.
    /// </summary>
    public static void Apply()
    {
        if (_modal != null && !_modal.Scale.Equals(DrawnScale))
        {
            while (Overlays.CloseNewest()) { }
            while (HelpLayer.Escape()) { }
            _modal.Scale = DrawnScale;
        }
        Changed?.Invoke();
    }

    /// <summary>Saves <paramref name="scale"/> as the player's choice and draws the window at it.</summary>
    public static void Set(double scale)
    {
        PanelState.Prefs.Scale = scale;
        PanelState.Save();
        Apply();
    }

    private static void RoomChanged(Vector2 room)
    {
        Room = room;
        var (width, height) = ScaleSteps.SmallestRoom;
        Cap = ScaleSteps.Cap(room.X, room.Y, Base, width, height);
        Apply();
    }

    public static void Reset()
    {
        _modal = null;
        _base = 1.00f;
        Room = Vector2.Zero;
        Cap = ScaleSteps.Largest;
        Changed = null;
    }
}
