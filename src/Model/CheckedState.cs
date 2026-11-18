namespace MelvorMods.CombatSimulator2.Model;

internal enum CheckedKind
{
    /// <summary>The game's build and version text match an entry CS2 was checked with.</summary>
    Listed,

    /// <summary>The build reads, but no entry matches it.</summary>
    Unlisted,

    /// <summary>The build can't be read, so it can't match an entry.</summary>
    Unknown,
}

/// <summary>Whether CS2 was checked with the game it runs in, for the session.</summary>
/// <param name="Build">The Steam build id, or <c>unknown</c>.</param>
/// <param name="Version">The game's version text, or null before the game can be asked for it.</param>
internal readonly record struct CheckedState(CheckedKind Kind, string Build, string? Version)
{
    public const string UnknownBuild = "unknown";

    /// <summary>
    /// The state from the manifest's entry for <paramref name="build"/> (its version text, or null when the build isn't
    /// listed) and the game's version text. Before the version text can be read, the build alone decides.
    /// </summary>
    public static CheckedState Of(string build, string? listedVersion, string? version) =>
        new(
            build == UnknownBuild ? CheckedKind.Unknown
                : listedVersion != null && (version == null || listedVersion == version) ? CheckedKind.Listed
                : CheckedKind.Unlisted,
            build,
            version
        );

    /// <summary>Results made now may be wrong, and the player is told so.</summary>
    public bool Shows => Kind != CheckedKind.Listed;

    /// <summary>The first line of <c>cs2 status</c>.</summary>
    public string StatusLine =>
        $"game build {Build} {Version ?? "?"}: "
        + Kind switch
        {
            CheckedKind.Listed => "checked",
            CheckedKind.Unlisted => "not checked",
            _ => "unknown",
        };
}
