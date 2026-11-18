namespace MelvorMods.Core;

/// <summary>
/// Which modals are open, top last. Godot-free so the Escape and pause-menu rules can be tested. Ids are unique
/// across mods (the owner is prefixed to each).
/// </summary>
internal sealed class ModalStack
{
    private readonly List<string> _open = [];
    private readonly Dictionary<string, Func<bool>> _escapes = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised with the id and its new state after a modal opens or closes (not on <see cref="Remove"/>).
    /// </summary>
    public event Action<string, bool>? Changed;

    /// <summary>Raised when a modal's Escape hook throws; the modal then closes as if it had no hook.</summary>
    public event Action<string, Exception>? EscapeFailed;

    public IReadOnlyList<string> Opened => _open;

    public bool IsOpen(string id) => _open.Contains(id, StringComparer.Ordinal);

    /// <summary>Opens <paramref name="id"/>, or brings it to the top if it is already open.</summary>
    public void Open(string id)
    {
        var was = _open.Remove(id);
        _open.Add(id);
        if (!was)
            Changed?.Invoke(id, true);
    }

    public bool Close(string id)
    {
        if (!_open.Remove(id))
            return false;
        Changed?.Invoke(id, false);
        return true;
    }

    /// <summary>Opens or closes <paramref name="id"/>; returns whether it is open afterwards.</summary>
    public bool Toggle(string id)
    {
        if (Close(id))
            return false;
        Open(id);
        return true;
    }

    /// <summary>Sets or clears (null) what <paramref name="id"/> does with Escape before it closes.</summary>
    public void SetEscape(string id, Func<bool>? hook)
    {
        if (hook == null)
            _escapes.Remove(id);
        else
            _escapes[id] = hook;
    }

    /// <summary>
    /// Handles Escape: the top modal's hook first, which keeps the modal open by returning true; otherwise the top
    /// modal closes. False when none is open, so the game handles the key as usual.
    /// </summary>
    public bool Cancel()
    {
        if (_open.Count == 0)
            return false;
        var top = _open[^1];
        if (_escapes.TryGetValue(top, out var hook))
        {
            try
            {
                if (hook())
                    return true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                EscapeFailed?.Invoke(top, e);
            }
        }
        return Close(top);
    }

    /// <summary>Closes every modal, top first, as the pause menu opens.</summary>
    public void CloseAll()
    {
        while (_open.Count > 0)
            Close(_open[^1]);
    }

    /// <summary>
    /// Forgets a modal and its Escape hook without raising <see cref="Changed"/> (its mod is unloading).
    /// </summary>
    public void Remove(string id)
    {
        _open.Remove(id);
        _escapes.Remove(id);
    }
}
