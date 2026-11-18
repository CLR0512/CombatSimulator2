namespace MelvorMods.Core;

/// <summary>
/// One mod button in the game's header. <see cref="Icon"/> is a Godot texture, kept as object for tests.
/// </summary>
internal sealed class HeaderEntry(
    string owner,
    string id,
    string label,
    object? icon,
    string? tooltip,
    Action onPressed
)
{
    public string Owner { get; } = owner;
    public string Id { get; } = id;
    public string Label { get; } = label;
    public object? Icon { get; set; } = icon;
    public string? Tooltip { get; } = tooltip;
    public Action OnPressed { get; } = onPressed;
    public bool Visible { get; set; } = true;

    /// <summary>The node's name in every header, so bridge scripts can find it.</summary>
    public string NodeName => Id + "Button";
}

/// <summary>The header buttons mods have added, in add order. Godot-free so the bookkeeping can be tested.</summary>
internal sealed class HeaderRegistry
{
    private readonly List<HeaderEntry> _entries = [];
    private readonly HashSet<string> _owners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after a button is added, removed, shown or hidden.</summary>
    public event Action? Changed;

    /// <summary>Raised with a shown button whose icon changed: the buttons stay as they are, with a new picture.</summary>
    public event Action<HeaderEntry>? IconChanged;

    /// <summary>Every mod that has added a button through this copy, kept after its buttons go.</summary>
    public IReadOnlyCollection<string> Owners => _owners;

    /// <summary>
    /// Whether a header child marked with <paramref name="owner"/> (null when unmarked) is one of this copy's
    /// buttons, so it may be removed when the buttons are placed again: only when a mod this copy has hosted
    /// placed it. Other copies' buttons and the game's own nodes stay.
    /// </summary>
    public static bool IsOwnButton(string? owner, IReadOnlyCollection<string> hosted)
    {
        ArgumentNullException.ThrowIfNull(hosted);
        return owner != null && hosted.Any(h => string.Equals(h, owner, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The visible buttons, in the order they were added.</summary>
    public IReadOnlyList<HeaderEntry> Shown => [.. _entries.Where(e => e.Visible)];

    /// <summary>Adds a button; one with the same id replaces it (a mod reloading).</summary>
    public HeaderEntry Add(HeaderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.RemoveAll(e => string.Equals(e.Id, entry.Id, StringComparison.Ordinal));
        _entries.Add(entry);
        _owners.Add(entry.Owner);
        Changed?.Invoke();
        return entry;
    }

    public void SetVisible(HeaderEntry entry, bool visible)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Visible == visible)
            return;
        entry.Visible = visible;
        if (_entries.Contains(entry))
            Changed?.Invoke();
    }

    public void SetIcon(HeaderEntry entry, object? icon)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (ReferenceEquals(entry.Icon, icon))
            return;
        entry.Icon = icon;
        if (_entries.Contains(entry) && entry.Visible)
            IconChanged?.Invoke(entry);
    }

    public void Remove(HeaderEntry entry)
    {
        if (_entries.Remove(entry))
            Changed?.Invoke();
    }

    /// <summary>Removes every button the mod added; returns how many.</summary>
    public int RemoveOwner(string owner)
    {
        var removed = _entries.RemoveAll(e => string.Equals(e.Owner, owner, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            Changed?.Invoke();
        return removed;
    }
}
