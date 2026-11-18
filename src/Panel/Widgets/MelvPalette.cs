using System.Globalization;
using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>A colour from mElvUI's palette, 0–1 per channel.</summary>
public readonly record struct PaletteColour(float R, float G, float B, float A);

/// <summary>How a role draws in one state: its fill and border, null where it draws none.</summary>
public readonly record struct RoleStyle(PaletteColour? Fill, PaletteColour? Border);

/// <summary>A palette, or why there is none.</summary>
public readonly record struct PaletteLoad(MelvPalette? Palette, Note? Reason);

/// <summary>
/// mElvUI's published palette (its <c>data/palette.json</c>, format 1), read as data: named colours, and per role
/// and state the colours a flat box fills and edges with, plus the border width and corner radius every box shares.
/// CS2 draws its mElvUI look from these, so it matches mElvUI and follows its palette updates. A file CS2 can't use
/// (missing, unreadable, another format, a role CS2 draws missing) gives a reason instead.
/// </summary>
public sealed class MelvPalette
{
    /// <summary>The palette format CS2 reads. mElvUI raises it when a role is renamed or removed.</summary>
    public const int Version = 1;

    /// <summary>The roles CS2 draws with.</summary>
    public static readonly IReadOnlyList<string> Roles =
    [
        "panel",
        "inset",
        "header",
        "button",
        "button-confirm",
        "button-cancel",
        "tab",
        "tooltip",
        "slot",
        "bar-track",
    ];

    private readonly Dictionary<string, Dictionary<string, RoleStyle>> _roles;
    private readonly Dictionary<string, PaletteColour> _colours;

    private MelvPalette(
        Dictionary<string, PaletteColour> colours,
        Dictionary<string, Dictionary<string, RoleStyle>> roles,
        float borderWidth,
        float cornerRadius
    )
    {
        _colours = colours;
        _roles = roles;
        BorderWidth = borderWidth;
        CornerRadius = cornerRadius;
    }

    public float BorderWidth { get; }
    public float CornerRadius { get; }

    /// <summary>Reads the palette file at <paramref name="path"/>.</summary>
    public static PaletteLoad Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(null, Note.Of("mElvUI's palette is missing"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(null, Note.Of("mElvUI's palette is unreadable: {0}", e.Message));
        }
        return Parse(text);
    }

    /// <summary>Reads a palette from its JSON text.</summary>
    public static PaletteLoad Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException e)
        {
            return new(null, Note.Of("mElvUI's palette is unreadable: {0}", e.Message));
        }
    }

    private static PaletteLoad Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return new(null, Note.Of("mElvUI's palette is unreadable: not an object"));
        var version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out var number) ? number : (int?)null;
        if (version != Version)
            return new(
                null,
                version is { } found
                    ? Note.Of("mElvUI's palette is version {0}, CS2 reads version {1}", found, Version)
                    : Note.Of("mElvUI's palette is version unknown, CS2 reads version {0}", Version)
            );

        var colours = new Dictionary<string, PaletteColour>(StringComparer.Ordinal);
        if (root.TryGetProperty("colours", out var colourList) && colourList.ValueKind == JsonValueKind.Object)
            foreach (var colour in colourList.EnumerateObject())
                if (colour.Value.ValueKind == JsonValueKind.String && ParseColour(colour.Value.GetString()!) is { } c)
                    colours[colour.Name] = c;

        if (!root.TryGetProperty("roles", out var roleList) || roleList.ValueKind != JsonValueKind.Object)
            return new(null, Note.Of("mElvUI's palette has no roles"));
        var roles = new Dictionary<string, Dictionary<string, RoleStyle>>(StringComparer.Ordinal);
        foreach (var role in roleList.EnumerateObject())
        {
            if (role.Value.ValueKind != JsonValueKind.Object)
                continue;
            var states = new Dictionary<string, RoleStyle>(StringComparer.Ordinal);
            foreach (var state in role.Value.EnumerateObject())
            {
                if (state.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (!Named(state.Value, "fill", colours, out var fill, out var missing))
                    return Unknown(role.Name, missing);
                if (!Named(state.Value, "border", colours, out var border, out missing))
                    return Unknown(role.Name, missing);
                states[state.Name] = new RoleStyle(fill, border);
            }
            roles[role.Name] = states;
        }
        foreach (var needed in Roles)
            if (!roles.TryGetValue(needed, out var states) || !states.ContainsKey("normal"))
                return new(null, Note.Of("mElvUI's palette has no '{0}' role", needed));

        return new(
            new MelvPalette(colours, roles, Number(root, "borderWidth", 1), Number(root, "cornerRadius", 0)),
            null
        );
    }

    private static PaletteLoad Unknown(string role, string colour) =>
        new(null, Note.Of("mElvUI's palette role '{0}' names an unknown colour '{1}'", role, colour));

    // A state's fill or border: a colour name, or null for none. False with the name when it isn't a colour.
    private static bool Named(
        JsonElement state,
        string slot,
        Dictionary<string, PaletteColour> colours,
        out PaletteColour? colour,
        out string missing
    )
    {
        colour = null;
        missing = "";
        if (!state.TryGetProperty(slot, out var value) || value.ValueKind != JsonValueKind.String)
            return true;
        var name = value.GetString()!;
        if (colours.TryGetValue(name, out var c))
        {
            colour = c;
            return true;
        }
        missing = name;
        return false;
    }

    private static float Number(JsonElement root, string name, float fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? (float)number
            : fallback;

    // #rrggbbaa (or #rrggbb, opaque).
    private static PaletteColour? ParseColour(string text)
    {
        if (text.Length is not (7 or 9) || text[0] != '#')
            return null;
        if (!uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return null;
        if (text.Length == 7)
            value = (value << 8) | 0xff;
        return new PaletteColour(
            ((value >> 24) & 0xff) / 255f,
            ((value >> 16) & 0xff) / 255f,
            ((value >> 8) & 0xff) / 255f,
            (value & 0xff) / 255f
        );
    }

    /// <summary>A named colour, such as <c>text</c>; null if the palette has none by that name.</summary>
    public PaletteColour? Colour(string name) => _colours.TryGetValue(name, out var colour) ? colour : null;

    /// <summary>
    /// How <paramref name="role"/> draws in <paramref name="state"/> (<c>normal</c>, <c>hover</c>, <c>pressed</c>,
    /// <c>hover_pressed</c>, <c>disabled</c>, <c>focus</c>). A state the role doesn't define falls back as mElvUI
    /// draws it: <c>hover_pressed</c> to <c>pressed</c>, then everything to <c>normal</c>.
    /// </summary>
    public RoleStyle Style(string role, string state)
    {
        if (!_roles.TryGetValue(role, out var states))
            return default;
        if (states.TryGetValue(state, out var style))
            return style;
        if (state == "hover_pressed" && states.TryGetValue("pressed", out style))
            return style;
        return states.TryGetValue("normal", out style) ? style : default;
    }
}
