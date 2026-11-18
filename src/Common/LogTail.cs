using System.Text;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>The end of a log another process may still be writing: read with shared access, from its last bytes.</summary>
internal static class LogTail
{
    /// <summary>The last <paramref name="maxBytes"/> of the file as text, starting at a whole line; null when missing.</summary>
    public static string? Text(string path, long maxBytes)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            var start = Math.Max(0, stream.Length - maxBytes);
            stream.Position = start;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            if (start > 0 && text.IndexOf('\n') is var cut and >= 0)
                text = text[(cut + 1)..];
            return text;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The file's last <paramref name="count"/> lines, read from at most its last 256 KB.</summary>
    public static string[] Lines(string path, int count)
    {
        var text = Text(path, 256 * 1024);
        if (text == null)
            return [];
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines[Math.Max(0, lines.Length - count)..];
    }
}
