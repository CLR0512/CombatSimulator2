using System.Text.Json;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// The kept comparisons, as plain JSON <c>{"format": 1, "comparisons": […]}</c> beside the run files, newest first,
/// written atomically. The file that held a single comparison before several were kept is taken over the first time
/// this one is missing.
/// </summary>
public static class ComparisonsFile
{
    public const int Format = 1;
    public const string Name = "comparisons.json";

    /// <summary>The file of the one comparison that was kept before there was a list.</summary>
    public const string SingleName = "comparison.json";

    private sealed class Envelope
    {
        public int Format { get; set; }
        public List<Comparison>? Comparisons { get; set; }
    }

    private sealed class SingleEnvelope
    {
        public int Format { get; set; }
        public Comparison? Comparison { get; set; }
    }

    public static string PathIn(string folder) => Path.Combine(folder, Name);

    public static void Write(string folder, IEnumerable<Comparison> comparisons)
    {
        ArgumentNullException.ThrowIfNull(comparisons);
        Directory.CreateDirectory(folder);
        var path = PathIn(folder);
        var temp = path + ".tmp";
        File.WriteAllBytes(
            temp,
            JsonSerializer.SerializeToUtf8Bytes(
                new Envelope { Format = Format, Comparisons = [.. comparisons] },
                RunFile.Options
            )
        );
        File.Move(temp, path, true);
    }

    /// <summary>
    /// The kept comparisons, newest first; empty with why when the file can't be used (unreadable, another format),
    /// and it is then left as it is. Without the file, the single comparison's file becomes the list: this file is
    /// written and that one deleted.
    /// </summary>
    public static (IReadOnlyList<Comparison> Comparisons, string? Problem) Read(string folder)
    {
        var path = PathIn(folder);
        if (!File.Exists(path))
            return TakeOverSingle(folder);
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path), RunFile.Options);
            if (envelope?.Format != Format)
                return ([], $"{Name}: format {envelope?.Format} isn't {Format}");
            return ([.. (envelope.Comparisons ?? []).Where(Usable)], null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return ([], $"{Name}: {e.Message}");
        }
    }

    private static (IReadOnlyList<Comparison> Comparisons, string? Problem) TakeOverSingle(string folder)
    {
        var single = Path.Combine(folder, SingleName);
        if (!File.Exists(single))
            return ([], null);
        try
        {
            var envelope = JsonSerializer.Deserialize<SingleEnvelope>(File.ReadAllBytes(single), RunFile.Options);
            if (envelope?.Format != Format)
                return ([], $"{SingleName}: format {envelope?.Format} isn't {Format}");
            if (envelope.Comparison is not { } comparison || !Usable(comparison))
                return ([], $"{SingleName}: no comparison");
            Write(folder, [comparison]);
            File.Delete(single);
            return ([comparison], null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return ([], $"{SingleName}: {e.Message}");
        }
    }

    private static bool Usable(Comparison? comparison) => comparison is { A.Length: > 0, B.Length: > 0 };
}
