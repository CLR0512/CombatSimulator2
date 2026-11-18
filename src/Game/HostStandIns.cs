using System.Collections.Concurrent;
using Godot;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The simulator host draws and plays nothing, yet every entity asks for its icon, its background and its sounds
/// while a copy loads its data: over a gigabyte the host would hold for its whole life. And the combat areas'
/// difficulties ask for the style of their label, which alone takes about four seconds to load in a host that
/// has loaded nothing yet: most of its first copy's build. There, a simulating thread's request for a texture, a
/// sound or a label style is not made, and fetching it gives one shared empty stand-in of that kind. The game's
/// own process loads as before, and so does a path that doesn't exist, which fails as it does in the game.
/// </summary>
internal static partial class HostStandIns
{
    private static readonly Lock _lock = new();

    // Every path asked for once: its stand-in, or null when the request goes to the game's loader as it is.
    private static readonly ConcurrentDictionary<string, Resource?> _byPath = new(StringComparer.Ordinal);
    private static Resource? _texture;
    private static Resource? _sound;
    private static Resource? _labelStyle;

    /// <summary>True when the request is answered with a stand-in and must not be made.</summary>
    internal static bool Request(string path, string typeHint)
    {
        if (!Guards.InHost || !Guards.Simulating)
            return false;
        var off = false;
        SwitchedOff(ref off);
        if (off)
            return false;
        if (_byPath.TryGetValue(path, out var known))
            return known != null;
        // Looked up outside the lock: copies built side by side ask for the same thousands of paths at once.
        Resource? standIn = null;
        if (typeHint is "Texture2D" or "AudioStream" or "LabelSettings" && ResourceLoader.Exists(path))
        {
            lock (_lock)
            {
                standIn = typeHint switch
                {
                    "Texture2D" => _texture ??= Shared(new PlaceholderTexture2D()),
                    "AudioStream" => _sound ??= Shared(new AudioStreamWav()),
                    _ => _labelStyle ??= Shared(new LabelSettings()),
                };
            }
        }
        _byPath[path] = standIn;
        return standIn != null;
    }

    internal static Resource? Get(string path) => Guards.InHost && _byPath.TryGetValue(path, out var r) ? r : null;

    // Every copy on every thread holds the same object: one extra native reference keeps its count from ever
    // dropping back to one, as for the resources the copies share.
    private static Resource Shared(Resource resource)
    {
        _ = resource.Reference();
        return resource;
    }

    // Dev builds only: a host started to load textures and sounds as the game does, to measure what stand-ins save.
    static partial void SwitchedOff(ref bool off);
}
