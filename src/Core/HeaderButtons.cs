using GamesByMalcs.Melvor2.CustomControls;
using GamesByMalcs.Melvor2.GameMenus.Header;
using Godot;
using HarmonyLib;

namespace MelvorMods.Core;

sealed partial class ModContext
{
    private ModHeader? _header;

    /// <summary>The game's header; buttons added here belong to this mod.</summary>
    public ModHeader Header => _header ??= CreateHeader();

    // The first use places the mod's buttons in every header, now and in headers made later, until it unloads.
    private ModHeader CreateHeader()
    {
        var header = new ModHeader(HeaderButtons.Registry, Id, Hooks);
        HeaderButtons.Start();
        OnReady(HeaderButtons.OnTreeReady);
        OnNodeAdded(HeaderButtons.OnNodeAdded);
        return header;
    }
}

/// <summary>A mod's handle on the game's header; buttons added here belong to the mod.</summary>
sealed class ModHeader
{
    private readonly HeaderRegistry _registry;
    private readonly string _owner;

    internal ModHeader(HeaderRegistry registry, string owner, ModHooks hooks)
    {
        _registry = registry;
        hooks.AddOwned(() => registry.RemoveOwner(owner));
        _owner = owner;
    }

    /// <summary>
    /// Adds a button right after the game's Guide button in every header, now and after each character load. It is
    /// the game's own header button scene, named <c>&lt;id&gt;Button</c>, showing <paramref name="label"/> under
    /// <paramref name="icon"/> (Guide's icon when null), with <paramref name="tooltip"/>. The label and tooltip are the
    /// mod's messages (mark them with <see cref="ModText.Key"/>): the mod shows them translated by the mod's catalogues
    /// and redraws them when the language changes. Unlike Guide it stays on every page. Several mod buttons follow
    /// Guide in the order they were added. If the header can't be read, the button is absent and a warning is logged
    /// once. The mod removes it when it unloads.
    /// </summary>
    public HeaderButton Add(string id, string label, Texture2D? icon, string? tooltip, Action onPressed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(onPressed);
        var entry = _registry.Add(new HeaderEntry(_owner, id, label, icon, tooltip, onPressed));
        return new HeaderButton(_registry, entry);
    }
}

/// <summary>A mod's header button; see <see cref="ModHeader.Add"/>.</summary>
sealed class HeaderButton
{
    private readonly HeaderRegistry _registry;
    private readonly HeaderEntry _entry;

    internal HeaderButton(HeaderRegistry registry, HeaderEntry entry)
    {
        _registry = registry;
        _entry = entry;
    }

    /// <summary>
    /// Whether the button shows, in every header at once. Hiding frees the nodes; showing adds them back in place.
    /// </summary>
    public bool Visible
    {
        get => _entry.Visible;
        set => _registry.SetVisible(_entry, value);
    }

    /// <summary>
    /// The icon (Guide's when null, which a button keeps once it has shown another); changing it changes the picture
    /// on the button in every header, without making the buttons again.
    /// </summary>
    public Texture2D? Icon
    {
        get => _entry.Icon as Texture2D;
        set => _registry.SetIcon(_entry, value);
    }

    public void Remove() => _registry.Remove(_entry);
}

/// <summary>
/// Places mod buttons after Guide in every live <see cref="GameHeader"/>, and tells the modal host where the header
/// ends. Each button is an instance of the game's header button scene, so it keeps the game's drawing, hover and
/// press handling, and skins style it like Guide. The game's names it relies on are the constants below.
/// </summary>
internal static class HeaderButtons
{
    private const string GuideProperty = "TutorialButton"; // GameHeader: the Guide button the mod buttons follow
    private const string ScenePath = "res://Tutorials/header_tutorial_button.tscn"; // Guide's own scene
    private const string TitleName = "Title"; // the label under the icon
    private const string IconName = "TextureRect"; // the icon

    internal const string Meta = "melvormods_header";

    private static readonly Dictionary<ulong, GameHeader> _headers = [];
    private static readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);
    private static HeaderRegistry? _registry;

    /// <summary>This copy's header buttons, from every mod it hosts.</summary>
    public static HeaderRegistry Registry { get; } = new();
    private static PackedScene? _scene;
    private static bool _sceneTried;

    public static void Start()
    {
        if (_registry != null)
            return;
        _registry = Registry;
        Registry.Changed += SyncAll;
        Registry.IconChanged += ShowIcon;
    }

    // The new picture on the button as it stands in each header: the icon follows a window opening and closing,
    // and placing every button again for that would cost the frame the window opens in.
    private static void ShowIcon(HeaderEntry entry)
    {
        if (entry.Icon is not Texture2D icon)
            return;
        foreach (var (id, header) in _headers.ToList())
        {
            if (!GodotObject.IsInstanceValid(header))
            {
                _headers.Remove(id);
                continue;
            }
            if (
                Guide(header)?.GetParent()?.FindChild(entry.NodeName, false, false) is { } button
                && button.FindChild(IconName, true, false) is TextureRect rect
            )
                rect.Texture = icon;
        }
    }

    /// <summary>Called for every node entering the tree: places buttons in a new header once it's ready.</summary>
    public static void OnNodeAdded(Node node)
    {
        if (node is not GameHeader header)
            return;
        if (header.IsNodeReady())
            Track(header);
        else
            header.Connect(
                Node.SignalName.Ready,
                Callable.From(() => Track(header)),
                (uint)GodotObject.ConnectFlags.OneShot
            );
    }

    /// <summary>Finds headers already in the tree (a mod loaded in game).</summary>
    public static void OnTreeReady(SceneTree tree)
    {
        var stack = new Stack<Node>();
        stack.Push(tree.Root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is GameHeader header && header.IsNodeReady())
            {
                Track(header);
                continue;
            }
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
    }

    /// <summary>The bottom edge of the header on screen, in canvas units; 0 when no header is showing.</summary>
    public static float HeaderBottom()
    {
        // Read every frame a modal is open, so it copies nothing: a header that is gone is dropped after the walk.
        float bottom = 0;
        ulong? gone = null;
        foreach (var (id, header) in _headers)
        {
            if (!GodotObject.IsInstanceValid(header) || !header.IsInsideTree())
                gone = id;
            else if (header.IsVisibleInTree())
                bottom = Math.Max(bottom, header.GetGlobalRect().End.Y);
        }
        if (gone is { } id2)
            _headers.Remove(id2);
        return bottom;
    }

    private static void Track(GameHeader header)
    {
        if (!GodotObject.IsInstanceValid(header))
            return;
        _headers[header.GetInstanceId()] = header;
        Sync(header);
    }

    private static void SyncAll()
    {
        try
        {
            foreach (var (id, header) in _headers.ToList())
            {
                if (GodotObject.IsInstanceValid(header))
                    Sync(header);
                else
                    _headers.Remove(id);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write("core", $"WARN header buttons not updated: {e}");
        }
    }

    // Takes out this header's mod buttons and puts the visible ones back after Guide, in order.
    private static void Sync(GameHeader header)
    {
        if (_registry == null || !GodotObject.IsInstanceValid(header))
            return;
        var shown = _registry.Shown;
        var guide = Guide(header);
        if (guide?.GetParent() is not { } parent)
        {
            foreach (var owner in shown.Select(e => e.Owner).Distinct())
                if (_warned.Add(owner))
                    Log.Write(owner, $"WARN header button not placed: no {GuideProperty} in the game's header");
            return;
        }
        // Only this copy's buttons: another mod's copy of this code places its own beside them.
        foreach (var child in parent.GetChildren())
        {
            var owner = child.HasMeta(Meta) ? child.GetMeta(Meta).AsString() : null;
            if (HeaderRegistry.IsOwnButton(owner, _registry.Owners))
            {
                parent.RemoveChild(child);
                child.QueueFree();
            }
        }
        var index = guide.GetIndex();
        foreach (var entry in shown)
        {
            var button = Make(entry, guide);
            if (button == null)
                continue;
            parent.AddChild(button);
            parent.MoveChild(button, ++index);
        }
    }

    private static Control? Guide(GameHeader header)
    {
        try
        {
            return AccessTools.Property(typeof(GameHeader), GuideProperty)?.GetValue(header) as Control;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write("core", $"WARN reading GameHeader.{GuideProperty} failed: {e.Message}");
            return null;
        }
    }

    // The game's header button scene with its script, or a copy of Guide (script included) if the scene is gone.
    private static Control? Make(HeaderEntry entry, Control guide)
    {
        Control? button = null;
        try
        {
            if (!_sceneTried)
            {
                _sceneTried = true;
                _scene = ResourceLoader.Exists(ScenePath) ? ResourceLoader.Load<PackedScene>(ScenePath) : null;
                if (_scene == null)
                    Log.Write("core", $"WARN {ScenePath} not found; header buttons copy Guide instead");
            }
            button =
                _scene?.Instantiate() as Control
                ?? guide.Duplicate((int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts)) as Control;
            if (button == null)
                throw new InvalidOperationException("the header button couldn't be made");
            button.Name = entry.NodeName;
            button.SetMeta(Meta, entry.Owner);
            button.Visible = true;
            // The label and tooltip are the mod's messages, in its domain and redrawn when the language changes.
            CoreLabels.Adopt(button, entry.Owner);
            if (button.FindChild(TitleName, true, false) is Label title)
            {
                title.AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled;
                CoreLabels.Set(title, entry.Owner, entry.Label);
            }
            if (entry.Icon is Texture2D icon && button.FindChild(IconName, true, false) is TextureRect rect)
                rect.Texture = icon;
            if (entry.Tooltip is { } tooltip)
                CoreLabels.Set(button, entry.Owner, tooltip, tooltip: true);
            else
                button.TooltipText = "";
            if (button is BaseButtonContainer pressable)
                pressable.Pressed += () => Press(entry);
            else
                throw new InvalidOperationException($"the header button is a {button.GetType().Name}, not a button");
            return button;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            button?.QueueFree();
            if (_warned.Add(entry.Owner))
                Log.Write(entry.Owner, $"WARN header button '{entry.Id}' not placed: {e.Message}");
            return null;
        }
    }

    private static void Press(HeaderEntry entry)
    {
        try
        {
            entry.OnPressed();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write(entry.Owner, $"ERROR header button '{entry.Id}' failed: {e}");
        }
    }
}
