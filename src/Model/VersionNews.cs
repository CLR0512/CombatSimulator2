namespace MelvorMods.CombatSimulator2.Model;

/// <summary>One change in a version, as the About window lists it: something new, or something fixed.</summary>
public sealed record NewsLine(bool Fixed, Note Text);

/// <summary>
/// What each version changed, in the player's words: written by hand before a release, under the version the
/// manifest will carry, new things before fixes. A version without an entry has nothing to tell.
/// </summary>
public static class VersionNews
{
    private static readonly Dictionary<string, NewsLine[]> Versions = new(StringComparer.Ordinal)
    {
        ["0.1.0-preview.1"] =
        [
            New(
                Note.Of("Simulate every monster, dungeon and Slayer task with your setup, ranked by XP, kills and more")
            ),
            New(Note.Of("Compare two setups on the same fights")),
            New(Note.Of("Try any gear, levels, prayers and Setup entries, owned or not")),
            New(Note.Of("Find the best setup for a target, with a cheaper and a safer one")),
            New(Note.Of("Rare drop chances per kill, and roughly when the first should drop")),
            New(Note.Of("Slayer masters: the best loadout for each, and which tasks to block")),
        ],
    };

    /// <summary>Every version with an entry.</summary>
    public static IReadOnlyCollection<string> Listed => Versions.Keys;

    /// <summary>The changes of <paramref name="version"/>; empty when it has no entry.</summary>
    public static IReadOnlyList<NewsLine> For(string version) =>
        version != null && Versions.TryGetValue(version, out var lines) ? lines : [];

    private static NewsLine New(Note text) => new(false, text);
}
