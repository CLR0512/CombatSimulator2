using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>Time with CS2's window open, and what was simulated.</summary>
public sealed class UsageTotals
{
    public double Seconds { get; set; }
    public long Runs { get; set; }
    public long Fights { get; set; }
    public long Comparisons { get; set; }
}

/// <summary>
/// What the About window says of the player's use of CS2: totals for this game launch (never saved) and for all time,
/// the day CS2 was first opened, and the version whose changes the player has seen. Kept as plain JSON
/// <c>{"format": 1, …}</c> in the mod's data folder, written atomically. A file from another version of CS2 is read
/// for the fields it has; one that can't be read starts the totals again, and never stops CS2.
/// </summary>
public sealed class UsageStats
{
    public const int Format = 1;
    public const string FileName = "usage.json";

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
        public UsageTotals? AllTime { get; set; }
        public DateTime? FirstOpened { get; set; }
        public string? SeenVersion { get; set; }
    }

    /// <summary>This game launch.</summary>
    public UsageTotals Session { get; } = new();

    public UsageTotals AllTime { get; private set; } = new();

    /// <summary>When CS2's window was first opened, in UTC; null before it ever was.</summary>
    public DateTime? FirstOpened { get; private set; }

    /// <summary>The version whose changes the player has seen; null before CS2 ever ran.</summary>
    public string? SeenVersion { get; private set; }

    /// <summary>Whether anything saved changed since the last <see cref="Write"/>.</summary>
    public bool Unsaved { get; private set; }

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    /// <summary>
    /// The file's content; empty when there is no file, and empty with why when it can't be read (the next write
    /// replaces it).
    /// </summary>
    public static (UsageStats Stats, string? Problem) Read(string folder)
    {
        var path = PathIn(folder);
        if (!File.Exists(path))
            return (new(), null);
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path), Options);
            if (envelope == null)
                return (new(), $"{FileName}: empty");
            var all = envelope.AllTime ?? new();
            var stats = new UsageStats
            {
                AllTime = new()
                {
                    Seconds = double.IsFinite(all.Seconds) ? Math.Max(0, all.Seconds) : 0,
                    Runs = Math.Max(0, all.Runs),
                    Fights = Math.Max(0, all.Fights),
                    Comparisons = Math.Max(0, all.Comparisons),
                },
                FirstOpened = envelope.FirstOpened?.ToUniversalTime(),
                SeenVersion = string.IsNullOrWhiteSpace(envelope.SeenVersion) ? null : envelope.SeenVersion,
            };
            return (stats, null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return (new(), $"{FileName}: {e.Message}");
        }
    }

    public void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = PathIn(folder);
        var temp = path + ".tmp";
        var envelope = new Envelope
        {
            Format = Format,
            AllTime = AllTime,
            FirstOpened = FirstOpened,
            SeenVersion = SeenVersion,
        };
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(envelope, Options));
        File.Move(temp, path, true);
        Unsaved = false;
    }

    /// <summary>
    /// Starts a game launch on <paramref name="version"/>: a player who never ran CS2 has nothing new to be told of,
    /// so that version counts as seen.
    /// </summary>
    public void StartOn(string version)
    {
        if (SeenVersion != null)
            return;
        SeenVersion = version;
        Unsaved = true;
    }

    /// <summary>Whether the player hasn't seen what <paramref name="version"/> changed.</summary>
    public bool Unseen(string version) => SeenVersion != version;

    /// <summary>Notes that the player has seen what <paramref name="version"/> changed.</summary>
    public void See(string version)
    {
        if (SeenVersion == version)
            return;
        SeenVersion = version;
        Unsaved = true;
    }

    /// <summary>Notes that CS2's window opened at <paramref name="now"/>; the first time is kept.</summary>
    public void Opened(DateTime now)
    {
        if (FirstOpened != null)
            return;
        FirstOpened = now.ToUniversalTime();
        Unsaved = true;
    }

    /// <summary>Adds time spent with the window open.</summary>
    public void AddTime(double seconds)
    {
        if (!(seconds > 0))
            return;
        Session.Seconds += seconds;
        AllTime.Seconds += seconds;
        Unsaved = true;
    }

    /// <summary>
    /// Counts a run that ended: the fights it simulated itself, however it ended (results taken from earlier runs
    /// were counted then), and one run when it finished.
    /// </summary>
    public void Count(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var fights = run.Results.Values.Where(r => r.ReusedFrom == null).Sum(r => (long)Math.Max(0, r.TrialsDone));
        var runs = run.State == RunState.Finished ? 1 : 0;
        if (fights == 0 && runs == 0)
            return;
        Session.Fights += fights;
        AllTime.Fights += fights;
        Session.Runs += runs;
        AllTime.Runs += runs;
        Unsaved = true;
    }

    /// <summary>
    /// Counts a comparison whose two sides both finished; its runs are counted as they end. One shown again with its
    /// sides exchanged was counted when it ran.
    /// </summary>
    public void Count(ComparedRuns comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        if (comparison.State != RunState.Finished || comparison.SwappedFrom != null)
            return;
        Session.Comparisons++;
        AllTime.Comparisons++;
        Unsaved = true;
    }
}
