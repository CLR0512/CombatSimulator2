using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>The four looks of one of the game's slots.</summary>
internal sealed record SlotStyles(StyleBox Normal, StyleBox Hover, StyleBox Pressed, StyleBox Disabled);

/// <summary>
/// The game's own look for its combat loadout column, read from its scenes as they are packed (never instanced): the
/// slot frames of its equipment, toolbelt and prayer slots, the column's panel, the line beside section titles, the
/// title text and its lock and empty-prayer icons. The what-if editor then draws what the game draws, and follows it
/// when the game changes; a part that can't be read falls back to the values the game shipped with. The mElvUI skin
/// restyles these as it restyles the game's own. Main thread.
/// </summary>
internal static class NativeStyles
{
    private const string EquipmentSlotScene = "res://Components/ItemEquipping/equipment_slot_view.tscn";
    private const string PrayerSlotScene = "res://Components/Prayers/prayer_slot_view.tscn";
    private const string CombatViewScene = "res://Entities/Combat/combat_active_view.tscn";
    private const string TitleSettingsPath = "res://Assets/Styles/LabelSettings/fs14_semibold_combat_title.tres";
    private const string LockIconPath = "res://Assets/Icons/lock_icon.png";
    private const string EmptyPrayerPath = "res://Assets/Icons/EmptyTickbox_64Artboard 1.png";

    private static SlotStyles? _slot;
    private static SlotStyles? _prayer;
    private static StyleBox? _column;
    private static Texture2D? _line;
    private static bool _lineRead;

    /// <summary>An equipment or toolbelt slot's frames.</summary>
    public static SlotStyles Slot =>
        _slot ??= Read(EquipmentSlotScene, "", ("NormalStyle", "HoverStyle", "PressedStyle", "DisabledStyle"));

    /// <summary>A prayer slot's frames (its own normal and hover; the rest as equipment slots).</summary>
    public static SlotStyles Prayer =>
        _prayer ??= Read(
            PrayerSlotScene,
            "",
            (
                "theme_override_styles/normal",
                "theme_override_styles/hover",
                "theme_override_styles/pressed",
                "theme_override_styles/disabled"
            )
        );

    /// <summary>The loadout column's dark panel.</summary>
    public static StyleBox Column =>
        _column ??=
            NodeProperty<StyleBox>(CombatViewScene, "EquipmentContainer", "theme_override_styles/panel", up: 3)
            ?? Flat("#0a0a0a", "#000000", 8);

    /// <summary>The thin line either side of a section title; null draws a plain one.</summary>
    public static Texture2D? TitleLine
    {
        get
        {
            if (!_lineRead)
            {
                _lineRead = true;
                _line = NodeProperty<Texture2D>(CombatViewScene, "EquipmentHeader/TextureRect2", "texture");
            }
            return _line;
        }
    }

    public static LabelSettings? TitleSettings => Art.Resource<LabelSettings>(TitleSettingsPath);

    public static Texture2D? LockIcon => Art.File(LockIconPath);

    public static Texture2D? EmptyPrayerIcon => Art.File(EmptyPrayerPath);

    /// <summary>
    /// Wraps <paramref name="panel"/> as a layer named <paramref name="name"/> over the page, with a soft shadow below
    /// it so it reads as on top of what it covers, the game's own panels being as dark as the page beneath.
    /// </summary>
    public static PanelContainer Raised(string name, Control panel)
    {
        var frame = new PanelContainer { Name = name };
        PixelBox.Panel(
            frame,
            new StyleBoxFlat
            {
                BgColor = Colors.Transparent,
                ShadowColor = new Color(0, 0, 0, 0.35f),
                ShadowSize = 8,
                ShadowOffset = new Vector2(0, 3),
            }
        );
        frame.AddChild(panel);
        return frame;
    }

    /// <summary>Forgets what was read, so a theme change reads it again.</summary>
    public static void Clear()
    {
        _slot = _prayer = null;
        _column = null;
        _line = null;
        _lineRead = false;
    }

    private static SlotStyles Read(
        string scene,
        string node,
        (string Normal, string Hover, string Pressed, string Disabled) names
    ) =>
        new(
            NodeProperty<StyleBox>(scene, node, names.Normal) ?? Flat("#1c1c1c", "#000000", 4),
            NodeProperty<StyleBox>(scene, node, names.Hover) ?? Flat("#5a4815", "#c9a43a", 4),
            NodeProperty<StyleBox>(scene, node, names.Pressed) ?? Flat("#151515", "#2a9ea3", 4),
            NodeProperty<StyleBox>(scene, node, names.Disabled) ?? Flat("#080808", "#1a1a1a", 4)
        );

    // A property of the node whose path ends with `node` ("" for the root), or of its ancestor `up` levels above.
    private static T? NodeProperty<T>(string scene, string node, string property, int up = 0)
        where T : class
    {
        var packed = Art.Resource<PackedScene>(scene);
        if (packed?.GetState() is not { } state)
        {
            Log.Warn($"{scene} missing; the what-if editor draws its own stand-in");
            return null;
        }
        for (var i = 0; i < state.GetNodeCount(); i++)
        {
            var path = state.GetNodePath(i).ToString();
            if (node.Length == 0 ? i != 0 : !path.EndsWith(node, StringComparison.Ordinal))
                continue;
            if (up > 0)
            {
                var target = string.Join('/', path.Split('/')[..^up]);
                for (var j = 0; j < state.GetNodeCount(); j++)
                {
                    if (state.GetNodePath(j).ToString() == target)
                        return Property<T>(state, j, property);
                }
                return null;
            }
            return Property<T>(state, i, property);
        }
        return null;
    }

    private static T? Property<T>(SceneState state, int node, string property)
        where T : class
    {
        for (var p = 0; p < state.GetNodePropertyCount(node); p++)
        {
            if (state.GetNodePropertyName(node, p) == property)
                return state.GetNodePropertyValue(node, p).Obj as T;
        }
        return null;
    }

    private static StyleBoxFlat Flat(string background, string border, int margin) =>
        new()
        {
            BgColor = Color.FromHtml(background),
            BorderColor = Color.FromHtml(border),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = margin,
            ContentMarginTop = margin,
            ContentMarginRight = margin,
            ContentMarginBottom = margin,
        };
}
