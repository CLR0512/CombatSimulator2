using Godot;

namespace MelvorMods.Core;

/// <summary>One large modal; see <see cref="ModModals.Register(string, Vector2, Func{Control})"/>. Main thread only.</summary>
sealed class ModModal
{
    internal ModModal(string key, string owner, Vector2 baseSize, Func<Control> build)
    {
        Key = key;
        Owner = owner;
        BaseSize = baseSize;
        Build = build;
    }

    internal string Key { get; }
    internal string Owner { get; }
    internal Vector2 BaseSize { get; }
    internal Func<Control> Build { get; }

    /// <summary>The modal's holder in the mod's modal layer, once built.</summary>
    internal Control? Slot { get; set; }
    internal Control? Content { get; set; }
    internal ColorRect? Dim { get; set; }
    internal bool Removed { get; set; }

    /// <summary>The room it was last laid out in on screen, less the margins, in the canvas's units.</summary>
    internal Vector2 Room { get; private set; }

    // While the room is being reported, a new scale is only stored: the layout running reads it next.
    private bool _reportingRoom;

    private float _scale = 1;

    /// <summary>
    /// How much larger the modal draws than its base size, 0.5–3 (1 by default), for big screens. The frame still
    /// fits inside the window's margins: at a large scale it gets fewer base units, not a bigger rect on screen
    /// than there is room for. Applies at once while open.
    /// </summary>
    public float Scale
    {
        get => _scale;
        set
        {
            var scale = Math.Clamp(float.IsFinite(value) ? value : 1, 0.5f, 3f);
            if (scale.Equals(_scale))
                return;
            _scale = scale;
            if (!_reportingRoom)
                ModalHost.Relayout(this);
        }
    }

    public bool IsOpen => ModalHost.Stack.IsOpen(Key);

    private Func<bool>? _escape;

    /// <summary>
    /// Called on Escape while this modal is the top one, before the mod closes it. Return true when the content handled
    /// the key (it closed something of its own); the modal then stays open. Null (the default) or false: the modal
    /// closes as before. An exception is logged and counts as false. Opening the pause menu closes modals without
    /// asking.
    /// </summary>
    public Func<bool>? Escape
    {
        get => _escape;
        set
        {
            _escape = value;
            if (!Removed)
                ModalHost.Stack.SetEscape(Key, value);
        }
    }

    /// <summary>
    /// Raised after the modal opens (true) or closes (false), by any route: code, Escape, the pause menu.
    /// </summary>
    public event Action<bool>? OpenChanged;

    /// <summary>
    /// Called on a left press in the dimmed room around the modal. The press goes no further, whether this is set or
    /// not, so it never reaches the game under the dim.
    /// </summary>
    public Action? OutsideClick { get; set; }

    public void Open()
    {
        if (!Removed)
            ModalHost.Stack.Open(Key);
    }

    public void Close() => ModalHost.Stack.Close(Key);

    public void Toggle()
    {
        if (!Removed)
            ModalHost.Stack.Toggle(Key);
    }

    /// <summary>
    /// Makes the content again: the current content is hidden at once and freed over the next frames, a little each
    /// frame so a large window doesn't stall one (so this is safe from the content's own handlers). The build runs
    /// again, at once into the same place if the modal is open, or on the next <see cref="Open"/> if it is closed.
    /// The modal stays open or closed; <see cref="OpenChanged"/> isn't raised.
    /// </summary>
    public void Rebuild()
    {
        if (!Removed)
            ModalHost.Rebuild(this);
    }

    internal void RaiseOpenChanged(bool open) => OpenChanged?.Invoke(open);

    /// <summary>
    /// Raised when the modal is laid out in a room on screen (less the margins, in the canvas's units) that differs
    /// from the last: the window was resized, the game's UI scale changed, or its sidebar opened or closed. A
    /// <see cref="Scale"/> set here is drawn by the layout that raised it.
    /// </summary>
    public event Action<Vector2>? RoomChanged;

    /// <summary>Notes the room and tells the handlers, if it changed. Returns once they have run.</summary>
    internal void ReportRoom(Vector2 room)
    {
        if (room == Room)
            return;
        Room = room;
        _reportingRoom = true;
        try
        {
            RoomChanged?.Invoke(room);
        }
        finally
        {
            _reportingRoom = false;
        }
    }
}
