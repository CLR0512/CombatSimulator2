using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// A named list of changes any character can use. The changes are kept as their text, so a preset saved by a later
/// CS2 with a kind this one doesn't know still reads, is listed, and says why it can't be used.
/// </summary>
public sealed record Preset(string Name, string Changes)
{
    /// <summary>The changes, or null with why their text can't be read by this CS2.</summary>
    public ChangeList? Read(out string? problem)
    {
        try
        {
            problem = null;
            return ChangeList.Parse(Changes);
        }
        catch (FormatException e)
        {
            problem = e.Message;
            return null;
        }
    }

    /// <summary>How many changes the text holds, readable or not.</summary>
    [JsonIgnore]
    public int Count => Changes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>What became of a request to save or rename a preset.</summary>
public enum PresetResult
{
    Saved,
    Replaced,

    /// <summary>Another preset has the name (in any case); nothing was changed.</summary>
    NameTaken,

    /// <summary>Nothing is left of the name once it is cleaned.</summary>
    NoName,
    NoChanges,

    /// <summary>The list holds <see cref="Presets.Max"/> presets already.</summary>
    Full,
    NotFound,
}

/// <summary>
/// The presets, as plain JSON <c>{"format": 1, "presets": [{"name", "changes"}]}</c> in the mod's data folder, in
/// the order they were saved, written atomically. Names are cleaned as run names are and unique whatever their case.
/// </summary>
public sealed class Presets
{
    public const int Format = 1;
    public const int Max = 100;
    public const string FileName = "presets.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private sealed class Envelope
    {
        public int Format { get; set; }
        public List<Preset>? Presets { get; set; }
    }

    private readonly List<Preset> _presets;

    private Presets(List<Preset> presets) => _presets = presets;

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    public IReadOnlyList<Preset> List => _presets;

    /// <summary>
    /// The file's presets; none when there is no file, and none with why when it can't be read or is in another
    /// format. Entries without a name, with a name already seen, or past the limit are left out.
    /// </summary>
    public static (Presets Presets, string? Problem) Read(string folder)
    {
        var path = PathIn(folder);
        if (!File.Exists(path))
            return (new([]), null);
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path), Options);
            if (envelope?.Format != Format)
                return (new([]), $"{FileName}: format {envelope?.Format} isn't {Format}");
            var presets = new Presets([]);
            foreach (var preset in envelope.Presets ?? [])
            {
                if (
                    preset?.Changes != null
                    && RunHistory.CleanName(preset.Name) is { } name
                    && presets.Find(name) == null
                    && presets._presets.Count < Max
                )
                    presets._presets.Add(new Preset(name, preset.Changes));
            }
            return (presets, null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
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
            JsonSerializer.SerializeToUtf8Bytes(new Envelope { Format = Format, Presets = _presets }, Options)
        );
        File.Move(temp, path, true);
    }

    /// <summary>The preset with this name, in any case; null for none.</summary>
    public Preset? Find(string? name)
    {
        var wanted = RunHistory.CleanName(name);
        return wanted == null
            ? null
            : _presets.Find(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Saves <paramref name="changes"/> under <paramref name="name"/>. A name already used is left alone unless
    /// <paramref name="replace"/> is set; a replaced preset keeps its place and takes the name as now written.
    /// </summary>
    public PresetResult Save(string? name, ChangeList changes, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (RunHistory.CleanName(name) is not { } clean)
            return PresetResult.NoName;
        if (changes.IsEmpty)
            return PresetResult.NoChanges;
        var preset = new Preset(clean, changes.ShortForm);
        var at = _presets.FindIndex(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
        {
            if (!replace)
                return PresetResult.NameTaken;
            _presets[at] = preset;
            return PresetResult.Replaced;
        }
        if (_presets.Count >= Max)
            return PresetResult.Full;
        _presets.Add(preset);
        return PresetResult.Saved;
    }

    /// <summary>Renames a preset; changing only the case of its own name is allowed.</summary>
    public PresetResult Rename(string? name, string? newName)
    {
        if (Find(name) is not { } preset)
            return PresetResult.NotFound;
        if (RunHistory.CleanName(newName) is not { } clean)
            return PresetResult.NoName;
        if (Find(clean) is { } other && !ReferenceEquals(other, preset))
            return PresetResult.NameTaken;
        _presets[_presets.IndexOf(preset)] = preset with { Name = clean };
        return PresetResult.Saved;
    }

    public bool Delete(string? name) => Find(name) is { } preset && _presets.Remove(preset);
}
