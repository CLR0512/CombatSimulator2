using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

public sealed class RunFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What a run list shows, written first in each file so it can be read without the results.</summary>
public sealed class RunHeader
{
    public string Id { get; set; } = "";
    public RunState State { get; set; }

    /// <summary>The setup's name (see <see cref="Model.Setup.Name"/>).</summary>
    public string Setup { get; set; } = "";
    public bool IgnoreRequirements { get; set; }
    public string GameBuild { get; set; } = "";
    public ulong Seed { get; set; }
    public int Targets { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    /// <summary>The changes the setup ran with, in short form; null for none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Changes { get; set; }

    /// <summary>The player's name for the run; null for none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>Whether the run is kept past the keep limit.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Pinned { get; set; }

    /// <summary>
    /// False when the run was made on a game build CS2 wasn't checked with, or one it couldn't read; left out when it
    /// was checked. A run saved before this was recorded reads as null: no mark.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Checked { get; set; }

    public static RunHeader Of(Run run) =>
        new()
        {
            Id = run.Id,
            State = run.State,
            Setup = run.Setup.Name,
            IgnoreRequirements = run.Setup.IgnoreRequirements,
            GameBuild = run.GameBuild,
            Seed = run.Seed,
            Targets = run.Targets.Count,
            StartedAt = run.StartedAt,
            EndedAt = run.EndedAt,
            Changes = run.Setup.Changes?.ShortForm,
            Name = run.Name,
            Pinned = run.Pinned,
            Checked = run.Checked,
        };
}

/// <summary>
/// A run on disk: gzip-compressed JSON <c>{"format": 3, "header": …, "run": …}</c>, written atomically. Format 3
/// adds what the game reported in the fights, which older files read without; format 2 keeps text as notes; format 1
/// (strings, and the header's setup with " (requirements ignored)") still reads, its text as verbatim notes.
/// </summary>
public static class RunFile
{
    public const int Format = 3;

    /// <summary>The oldest format still read.</summary>
    private const int OldestFormat = 1;
    public const string Extension = ".json.gz";

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new BigIntegerJson(),
            new ReadOnlySetJson(),
        },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private sealed class Envelope
    {
        public int Format { get; set; }
        public RunHeader? Header { get; set; }
        public Run? Run { get; set; }
    }

    /// <summary>The header as JSON, as it is written at the top of a file.</summary>
    public static string HeaderJson(Run run) => JsonSerializer.Serialize(RunHeader.Of(run), Options);

    /// <summary>
    /// Reads only the header at the top of a run file: the decompressed start is enough, so listing many runs
    /// never parses their results. Throws <see cref="RunFileException"/> like <see cref="Read"/>.
    /// </summary>
    public static RunHeader ReadHeader(string path)
    {
        try
        {
            var start = ReadStart(path);
            var reader = new Utf8JsonReader(start, isFinalBlock: false, default);
            int? format = null;
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new RunFileException($"{Path.GetFileName(path)}: unreadable: not a run file");
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (name == "format")
                {
                    format = reader.TryGetInt32(out var f) ? f : 0;
                    if (format is not (>= OldestFormat and <= Format))
                        throw new RunFileException($"{Path.GetFileName(path)}: format {format}, expected {Format}");
                }
                else if (name == "header" && format != null)
                    return Upgraded(
                        JsonSerializer.Deserialize<RunHeader>(ref reader, Options)
                            ?? throw new RunFileException($"{Path.GetFileName(path)}: no header")
                    );
                else
                    reader.Skip();
            }
            throw new RunFileException($"{Path.GetFileName(path)}: unreadable: no header");
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new RunFileException($"{Path.GetFileName(path)}: unreadable: {e.Message}", e);
        }
    }

    /// <summary>
    /// The character a run was simulated for, from the start of its file like <see cref="ReadHeader"/> (the header
    /// doesn't name it); null when the file can't be read or doesn't say.
    /// </summary>
    public static string? ReadCharacter(string path)
    {
        try
        {
            var reader = new Utf8JsonReader(ReadStart(path), isFinalBlock: false, default);
            // The run's setup is its second property, right after the header.
            return
                reader.Read()
                && Enter(ref reader, "run")
                && Enter(ref reader, "setup")
                && Enter(ref reader, "character")
                && reader.TokenType == JsonTokenType.String
                ? reader.GetString()
                : null;
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    // With the reader on an object's start: moves to the value of its property <name>; false when there is none
    // in the part of the file that was read.
    private static bool Enter(ref Utf8JsonReader reader, string name)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            return false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var found = reader.ValueTextEquals(name);
            if (!reader.Read())
                return false;
            if (found)
                return true;
            if (!reader.TrySkip())
                return false;
        }
        return false;
    }

    // The first 64 KB of a run file, decompressed: the header and the start of the run.
    private static ReadOnlySpan<byte> ReadStart(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var buffer = new byte[64 * 1024];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = gzip.Read(buffer, length, buffer.Length - length)) > 0)
            length += read;
        return buffer.AsSpan(0, length);
    }

    /// <summary>Writes <paramref name="run"/> to a temp file then renames it over <paramref name="path"/>.</summary>
    public static void Write(string path, Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var problems = run.Results.Values.SelectMany(r => r.Problems()).ToList();
        if (problems.Count > 0)
            throw new RunFileException($"{run.Id} breaks the model: {string.Join("; ", problems)}");
        var temp = path + ".tmp";
        try
        {
            using (var file = File.Create(temp))
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                JsonSerializer.Serialize(
                    gzip,
                    new Envelope
                    {
                        Format = Format,
                        Header = RunHeader.Of(run),
                        Run = run,
                    },
                    Options
                );
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new RunFileException($"{Path.GetFileName(path)}: can't write: {e.Message}", e);
        }
    }

    /// <summary>Reads a run. Throws <see cref="RunFileException"/> for unreadable files and other formats.</summary>
    public static Run Read(string path)
    {
        Envelope? envelope;
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gzip);
            var format = doc.RootElement.TryGetProperty("format", out var f) && f.TryGetInt32(out var n) ? n : 0;
            if (format is not (>= OldestFormat and <= Format))
                throw new RunFileException($"{Path.GetFileName(path)}: format {format}, expected {Format}");
            envelope = doc.RootElement.Deserialize<Envelope>(Options);
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new RunFileException($"{Path.GetFileName(path)}: unreadable: {e.Message}", e);
        }
        return envelope?.Run ?? throw new RunFileException($"{Path.GetFileName(path)}: no run");
    }

    // Format 1 marked an ignored-requirements setup in the header's label; format 2 keeps a flag.
    private static RunHeader Upgraded(RunHeader header)
    {
        if (header.Setup.EndsWith(Setup.RequirementsIgnoredSuffix, StringComparison.Ordinal))
        {
            header.Setup = header.Setup[..^Setup.RequirementsIgnoredSuffix.Length];
            header.IgnoreRequirements = true;
        }
        return header;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary file left behind is overwritten by the next save of the same run.
        }
    }
}
