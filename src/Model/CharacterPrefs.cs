using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// What CS2 remembers for one character: the targets it ignores and its two sides. <c>B</c> is null when the
/// character isn't comparing.
/// </summary>
public sealed class CharacterState
{
    private List<string> _ignored = [];
    private SideState _a = SideState.AsNow;

    /// <summary>The character's name when it was last seen, for a reader of the file.</summary>
    public string Name { get; set; } = "";

    /// <summary>Keys of targets left out of runs until the player brings them back.</summary>
    public List<string> Ignored
    {
        get => _ignored;
        set => _ignored = [.. (value ?? []).Distinct(StringComparer.Ordinal)];
    }

    public SideState A
    {
        get => _a;
        set => _a = value ?? SideState.AsNow;
    }

    public SideState? B { get; set; }

    /// <summary>The side the Setup tab is on.</summary>
    public SideLetter Editing { get; set; }
}

/// <summary>
/// Every character's state, as plain JSON <c>{"format": 1, "characters": {"&lt;key&gt;": …}}</c> in the mod's
/// data folder, written atomically. A character is keyed by its save's id, so a rename keeps its state.
/// </summary>
public sealed class CharacterPrefs
{
    public const int Format = 1;
    public const string FileName = "characters.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    private sealed class Envelope
    {
        public int Format { get; set; }
        public Dictionary<string, CharacterState>? Characters { get; set; }
    }

    private readonly Dictionary<string, CharacterState> _characters;

    private CharacterPrefs(Dictionary<string, CharacterState> characters) => _characters = characters;

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    public IReadOnlyCollection<string> Keys => _characters.Keys;

    /// <summary>
    /// The file's content; empty when there is no file, and empty with why when it can't be read or is in another
    /// format (the next write replaces it).
    /// </summary>
    public static (CharacterPrefs Prefs, string? Problem) Read(string folder)
    {
        var path = PathIn(folder);
        if (!File.Exists(path))
            return (new([]), null);
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path), Options);
            if (envelope?.Format != Format)
                return (new([]), $"{FileName}: format {envelope?.Format} isn't {Format}");
            var characters = new Dictionary<string, CharacterState>(StringComparer.Ordinal);
            foreach (var (key, state) in envelope.Characters ?? [])
            {
                if (state != null)
                    characters[key] = state;
            }
            return (new(characters), null);
        }
        // A side that can't be (a saved run with changes) or a change that isn't one is a damaged file too.
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (new([]), $"{FileName}: {e.Message}");
        }
    }

    public void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = PathIn(folder);
        var temp = path + ".tmp";
        File.WriteAllBytes(
            temp,
            JsonSerializer.SerializeToUtf8Bytes(new Envelope { Format = Format, Characters = _characters }, Options)
        );
        File.Move(temp, path, true);
    }

    /// <summary>
    /// The state of the character <paramref name="key"/>. The first time a character is seen
    /// (<paramref name="seeded"/>), its ignored targets, its side's base and its requirements flag start from what
    /// <paramref name="seed"/> held for every character before state was kept per character.
    /// </summary>
    public CharacterState For(string key, string name, Preferences seed, out bool seeded)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(seed);
        seeded = !_characters.TryGetValue(key, out var state);
        if (state == null)
        {
            state = new CharacterState
            {
                Ignored = [.. seed.DisabledTargets ?? []],
                A = new SideState(SideBase.Parse(seed.Setup), null, seed.IgnoreRequirements ?? false),
            };
            _characters[key] = state;
        }
        state.Name = name;
        return state;
    }

    /// <summary>
    /// Drops the character's ignores for targets the build no longer has. True if any was dropped.
    /// </summary>
    public bool DropUnknown(string key, IReadOnlySet<string> known)
    {
        ArgumentNullException.ThrowIfNull(known);
        return _characters.TryGetValue(key, out var state) && state.Ignored.RemoveAll(k => !known.Contains(k)) > 0;
    }
}
