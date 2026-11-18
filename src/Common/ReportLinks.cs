namespace MelvorMods.CombatSimulator2.Common;

/// <summary>Where a player sends a bug report.</summary>
internal static class ReportLinks
{
    /// <summary>CS2's repository, <c>owner/repo</c>; empty turns the GitHub button off.</summary>
    public const string Repository = "Crashv0/CombatSimulator2";

    /// <summary>The issue form a report's link opens, in the repository's <c>.github/ISSUE_TEMPLATE</c>.</summary>
    public const string IssueForm = "bug-report.yml";

    /// <summary>The Melvor Discord's channel for mod bug reports.</summary>
    public const string DiscordChannel = "https://discord.com/channels/625838709203271680/1029950790254731334";

    /// <summary>
    /// The private route: a direct message to CS2's developer on Discord, for a report whose save shouldn't be public.
    /// </summary>
    public const string DirectMessage = "https://discord.com/users/187106829984202761";
}
