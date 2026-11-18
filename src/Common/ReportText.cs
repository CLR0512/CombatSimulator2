using System.Text;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>
/// A report's summary as text: plain, as a Discord message and as a GitHub issue link. Each keeps to its place's
/// limits by cutting the error's text first, with <c>…</c>. The summary given is already redacted.
/// </summary>
internal static class ReportText
{
    public const int TitleLimit = 100;
    public const int DiscordLimit = 2000;
    public const int LinkLimit = 8000;
    private const string Product = "Combat Simulator 2 ";

    public static string Summary(ReportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var run = summary.LastRunId is { } id ? $"{id} ({summary.LastRunState})" : "none";
        return $"""
            CS2 version: {summary.Cs2Version}
            Game build: {summary.GameBuild}
            Last run: {run}
            Last error: {summary.LastError ?? "none"}
            Report: {summary.ReportFile}
            """;
    }

    /// <summary>A title, then the summary in a code block, then what to do: under 2,000 characters.</summary>
    public static string Discord(ReportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return Fit(summary, s => DiscordText(s).Length, DiscordLimit - 1, DiscordText);
    }

    /// <summary>
    /// A link opening the repository's issue form with the title and summary filled in, under 8,000 characters; null
    /// while there is no repository.
    /// </summary>
    public static string? IssueLink(ReportSummary summary, string repository)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (string.IsNullOrWhiteSpace(repository))
            return null;
        string Link(ReportSummary s) =>
            $"https://github.com/{repository}/issues/new?template={ReportLinks.IssueForm}"
            + $"&title={Uri.EscapeDataString(Title(s)[Product.Length..])}"
            + $"&summary={Uri.EscapeDataString(Summary(s))}";
        return Fit(summary, s => Link(s).Length, LinkLimit - 1, Link);
    }

    /// <summary>The first line: names the version and the error, at most 100 characters.</summary>
    public static string Title(ReportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var head = $"{Product}{summary.Cs2Version} bug report: ";
        var error = summary.LastError is { } e ? Short(e) : "no error";
        return head + Cut(error, TitleLimit - head.Length);
    }

    private static string DiscordText(ReportSummary summary) =>
        new StringBuilder()
            .Append(Title(summary))
            .Append("\n```text\n")
            .Append(Summary(summary))
            .Append("\n```\n")
            .Append("Describe what happened here, then drag in ")
            .Append(summary.ReportFile)
            .Append('.')
            .ToString();

    // The error's kind and reason read as one phrase: "host crash, the simulator closed unexpectedly".
    private static string Short(string error)
    {
        var colon = error.IndexOf(": ", StringComparison.Ordinal);
        return colon < 0 ? error : error[..colon] + ", " + error[(colon + 2)..];
    }

    // The text with the error cut until it fits; the error is the only part that can be long.
    private static string Fit(
        ReportSummary summary,
        Func<ReportSummary, int> length,
        int limit,
        Func<ReportSummary, string> render
    )
    {
        if (length(summary) <= limit || summary.LastError is not { } error)
            return render(summary);
        var low = 0;
        var high = error.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (length(summary with { LastError = Cut(error, mid) }) <= limit)
                low = mid;
            else
                high = mid - 1;
        }
        return render(summary with { LastError = Cut(error, low) });
    }

    private static string Cut(string text, int max) =>
        text.Length <= max ? text
        : max <= 1 ? "…"
        : text[..(max - 1)].TrimEnd() + "…";
}
