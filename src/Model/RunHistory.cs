using System.Globalization;
using System.Text;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// The rules for kept runs: names, when a run may be deleted, and the warning for a run from another build.
/// </summary>
public static class RunHistory
{
    public const int MaxNameLength = 40;

    /// <summary>
    /// A name as kept: trimmed, without control characters, at most 40 characters; null when nothing is left.
    /// </summary>
    public static string? CleanName(string? text)
    {
        if (text == null)
            return null;
        var kept = new StringBuilder(text.Length);
        foreach (var c in text)
            if (!char.IsControl(c))
                kept.Append(c);
        var name = kept.ToString().Trim();
        if (name.Length > MaxNameLength)
        {
            // Cut between whole characters, so a pair of surrogates is never split.
            var info = new StringInfo(name);
            var length = 0;
            var count = 0;
            while (count < info.LengthInTextElements)
            {
                var next = info.SubstringByTextElements(count, 1).Length;
                if (length + next > MaxNameLength)
                    break;
                length += next;
                count++;
            }
            name = name[..length].TrimEnd();
        }
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Why <paramref name="header"/>'s run may not be deleted, or null when it may: the run in progress, a pinned run,
    /// and the runs of every kept comparison (<paramref name="comparisonIds"/>) stay.
    /// </summary>
    public static Note? CanDelete(RunHeader header, string? inProgressId, IReadOnlySet<string> comparisonIds)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(comparisonIds);
        if (header.Id == inProgressId)
            return Note.Of("the run is in progress");
        if (header.Pinned)
            return Note.Of("the run is pinned: unpin it first");
        if (comparisonIds.Contains(header.Id))
            return Note.Of("a kept comparison uses this run");
        return null;
    }

    /// <summary>
    /// The comparison's own warnings when <paramref name="run"/> came from another game build than
    /// <paramref name="gameBuild"/> or another CS2 build than <paramref name="cs2Mvid"/>; null when both match.
    /// </summary>
    public static IReadOnlyList<Note>? BuildWarning(Run run, string gameBuild, string cs2Mvid)
    {
        ArgumentNullException.ThrowIfNull(run);
        var now = new Run { GameBuild = gameBuild, Cs2Mvid = cs2Mvid };
        var notes = ComparisonWarnings.Of(run, now, cs2Mvid);
        return notes.Count == 0 ? null : notes;
    }
}
