using System.IO.Compression;
using System.Text.Json;

namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// The latest answer per kind of search and target, in <c>searches.json.gz</c> beside the runs: gzip-compressed JSON
/// <c>{"format": 2, "answers": […]}</c>, written atomically. Not saved runs: nothing here counts towards their
/// limit. Format 1, from before setup searches, loads as gear searches. A file of a format this build doesn't know is
/// read as empty and never written over.
/// </summary>
public sealed class AnswerStore
{
    public const int Format = 2;
    private const int Oldest = 1;
    public const string Name = "searches.json.gz";

    /// <summary>The most answers kept; past it the oldest by when it was found goes.</summary>
    public const int Most = 50;

    private readonly string _path;
    private readonly List<Entry> _entries = [];
    private bool _foreign;

    private sealed class Envelope
    {
        public int Format { get; set; }
        public List<Entry>? Answers { get; set; }
    }

    private sealed class Entry
    {
        public string Kind { get; set; } = "";
        public string Target { get; set; } = "";
        public AnswerKey Key { get; set; } = new();
        public Answer Answer { get; set; } = new();
    }

    public AnswerStore(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        _path = Path.Combine(folder, Name);
    }

    public string FilePath => _path;

    public int Count => _entries.Count;

    /// <summary>The kind as the file names it.</summary>
    public static string KindName(SearchKind kind) => kind == SearchKind.Droptimizer ? "droptimizer" : "gear";

    /// <summary>
    /// Reads the file; returns why it couldn't be used, or null. Without a file the store is empty.
    /// </summary>
    public string? Load()
    {
        _entries.Clear();
        _foreign = false;
        if (!File.Exists(_path))
            return null;
        try
        {
            using var file = File.OpenRead(_path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            var envelope = JsonSerializer.Deserialize<Envelope>(gzip, RunFile.Options);
            if (envelope == null || envelope.Format is < Oldest or > Format)
            {
                _foreign = true;
                return $"{Name}: format {envelope?.Format} isn't {Format}";
            }
            _entries.AddRange((envelope.Answers ?? []).Where(e => e.Target.Length > 0 && e.Answer.Finished));
            return null;
        }
        catch (Exception e)
            when (e is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"{Name}: {e.Message}";
        }
    }

    /// <summary>
    /// Keeps <paramref name="answer"/> as the latest for its kind and target and writes the file. An answer of a
    /// search that didn't run to its end is not kept, and what was kept before it stays.
    /// </summary>
    public void Keep(Answer answer)
    {
        if (Add(answer))
            Write([.. _entries]);
    }

    /// <summary>
    /// As <see cref="Keep"/>, but the file is written on a thread of its own, after any write still under way: a
    /// search ends on the game's frame, and writing every kept answer there costs a frame of its own.
    /// <see cref="WriteFailed"/> hears of a write that failed.
    /// </summary>
    public void KeepLater(Answer answer)
    {
        if (!Add(answer))
            return;
        List<Entry> entries = [.. _entries];
        lock (_writing)
            _written = _written.ContinueWith(_ => WriteOrSay(entries), TaskScheduler.Default);
    }

    /// <summary>Told of a write <see cref="KeepLater"/> started that failed.</summary>
    public Action<Exception>? WriteFailed { get; set; }

    /// <summary>Waits for the writes <see cref="KeepLater"/> started.</summary>
    public void Flush()
    {
        Task written;
        lock (_writing)
            written = _written;
        written.Wait();
    }

    private readonly Lock _writing = new();
    private Task _written = Task.CompletedTask;

    private void WriteOrSay(List<Entry> entries)
    {
        try
        {
            Write(entries);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            WriteFailed?.Invoke(e);
        }
    }

    // Puts the answer in the list; false for one that isn't kept.
    private bool Add(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (!answer.Finished)
            return false;
        var kind = KindName(answer.Kind);
        _entries.RemoveAll(e =>
            e.Kind == kind
            && e.Target == answer.Target
            && e.Key.Character == answer.Key.Character
            && e.Key.SamePool(answer.Key)
        );
        _entries.Add(
            new Entry
            {
                Kind = kind,
                Target = answer.Target,
                Key = answer.Key,
                Answer = answer,
            }
        );
        while (_entries.Count > Most)
            _entries.Remove(_entries.MinBy(e => e.Answer.FoundAt)!);
        return true;
    }

    /// <summary>
    /// The kept answer for a kind and target of <paramref name="now"/>'s character and pool, with what differs between the key
    /// it was searched under and <paramref name="now"/>: empty when it is current, else why it is out of date. Null
    /// when none is kept.
    /// </summary>
    public (Answer Answer, IReadOnlyList<Note> Changed)? Find(SearchKind kind, string target, AnswerKey now)
    {
        ArgumentNullException.ThrowIfNull(now);
        var name = KindName(kind);
        var entry = _entries.Find(e =>
            e.Kind == name && e.Target == target && e.Key.Character == now.Character && e.Key.SamePool(now)
        );
        return entry == null ? null : (entry.Answer, entry.Key.Changed(now));
    }

    /// <summary>Every kept answer, the newest first.</summary>
    public IReadOnlyList<Answer> All => [.. _entries.Select(e => e.Answer).OrderByDescending(a => a.FoundAt)];

    /// <summary>
    /// What a bug report says of the kept answers: for each, what was searched and how it ended, never a result's
    /// batches.
    /// </summary>
    public IReadOnlyList<Dictionary<string, object?>> Summaries() =>
        [
            .. All.Select(answer => new Dictionary<string, object?>
            {
                ["kind"] = KindName(answer.Kind),
                ["target"] = answer.Target,
                ["state"] = answer.State.ToString(),
                ["foundAt"] = answer.FoundAt,
                ["metric"] = answer.Key.Goal.Metric,
                ["deaths"] = answer.Key.Goal.Deaths.ToString(),
                ["margin"] = answer.Margin,
                ["seed"] = answer.Seed,
                ["possible"] = answer.Possible,
                ["simulated"] = answer.Simulated,
                ["climbed"] = answer.Climbed,
                ["reran"] = answer.Reran,
                ["best"] = answer.Best?.Changes.ShortForm,
                ["ties"] = answer.Ties.Count,
                ["gains"] = answer.Gains.Count,
                ["gameBuild"] = answer.Key.GameBuild,
            }),
        ];

    /// <summary>
    /// The answer of a kind <paramref name="character"/> found last, whatever its target; null when none is kept.
    /// </summary>
    public Answer? Latest(SearchKind kind, string character)
    {
        var name = KindName(kind);
        return _entries.Where(e => e.Kind == name && e.Key.Character == character).MaxBy(e => e.Answer.FoundAt)?.Answer;
    }

    /// <summary>Forgets every answer and removes the file, whatever its format.</summary>
    public void Clear()
    {
        Flush();
        _entries.Clear();
        _foreign = false;
        File.Delete(_path);
    }

    private void Write(List<Entry> entries)
    {
        if (_foreign)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        using (var file = File.Create(temp))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            JsonSerializer.Serialize(gzip, new Envelope { Format = Format, Answers = entries }, RunFile.Options);
        File.Move(temp, _path, true);
    }
}
