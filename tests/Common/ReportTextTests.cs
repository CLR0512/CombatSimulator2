using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ReportTextTests
{
    internal static ReportSummary Crash(string? error = null) =>
        new(
            "0.1.0",
            "26316041 0.21.137",
            "run-20261109-140209-1",
            "Error",
            error ?? "host crash: the simulator closed unexpectedly (code -1073740791), phase fight",
            "CombatSimulator2-report-20261109-140312.zip"
        );

    [Fact]
    public void TheSummaryIsFiveLinesInOrder()
    {
        Assert.Equal(
            """
            CS2 version: 0.1.0
            Game build: 26316041 0.21.137
            Last run: run-20261109-140209-1 (Error)
            Last error: host crash: the simulator closed unexpectedly (code -1073740791), phase fight
            Report: CombatSimulator2-report-20261109-140312.zip
            """,
            ReportText.Summary(Crash())
        );
    }

    [Fact]
    public void WithoutAFailureOrARunTheyReadNone()
    {
        var text = ReportText.Summary(new ReportSummary("0.1.0", "b", null, null, null, "r.zip"));
        Assert.Contains("Last run: none\n", text, StringComparison.Ordinal);
        Assert.Contains("Last error: none\n", text, StringComparison.Ordinal);
    }

    private static readonly string LongError = "host crash: " + new string('x', 10_000);

    [Fact]
    public void TheDiscordMessageStartsWithATitleAndHoldsTheSummaryInABlock()
    {
        var text = ReportText.Discord(Crash());
        var lines = text.Split('\n');

        Assert.Equal(
            "Combat Simulator 2 0.1.0 bug report: host crash, the simulator closed unexpectedly (code -107374079…",
            lines[0]
        );
        Assert.Equal("```text", lines[1]);
        Assert.Equal(ReportText.Summary(Crash()), string.Join('\n', lines[2..7]));
        Assert.Equal("```", lines[7]);
        Assert.Equal(
            "Describe what happened here, then drag in CombatSimulator2-report-20261109-140312.zip.",
            lines[8]
        );
    }

    [Fact]
    public void TheTitleKeepsToItsLimitCuttingTheError()
    {
        var title = ReportText.Title(Crash(LongError));
        Assert.Equal(ReportText.TitleLimit, title.Length);
        Assert.EndsWith("…", title, StringComparison.Ordinal);
        Assert.Equal(
            "Combat Simulator 2 0.1.0 bug report: no error",
            ReportText.Title(Crash() with { LastError = null })
        );
    }

    [Fact]
    public void ALongErrorIsCutSoTheWholeMessageFitsDiscord()
    {
        var text = ReportText.Discord(Crash(LongError));

        Assert.True(text.Length < ReportText.DiscordLimit, $"{text.Length} characters, fence included");
        Assert.True(text.Length > ReportText.DiscordLimit - 20, "only as much is cut as needed");
        Assert.Contains("Last error: host crash: xxx", text, StringComparison.Ordinal);
        Assert.Contains("…", text, StringComparison.Ordinal);
        Assert.EndsWith(".zip.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIssueLinkOpensTheFormWithTitleAndSummaryEncoded()
    {
        var link = ReportText.IssueLink(Crash("run & #1: ünïcode\nline"), "Crashv0/CombatSimulator2");

        Assert.NotNull(link);
        Assert.StartsWith(
            "https://github.com/Crashv0/CombatSimulator2/issues/new?template=bug-report.yml&title=0.1.0%20bug%20report%3A%20run%20%26%20%231%2C%20",
            link,
            StringComparison.Ordinal
        );
        Assert.Contains("%C3%BCn%C3%AFcode%0Aline", link, StringComparison.Ordinal);
        Assert.Contains("&summary=CS2%20version%3A%200.1.0%0A", link, StringComparison.Ordinal);
        Assert.DoesNotContain("labels=", link, StringComparison.Ordinal);
        Assert.DoesNotContain("assignees=", link, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongErrorIsCutSoTheLinkFits()
    {
        var link = ReportText.IssueLink(Crash(LongError), "o/r")!;
        Assert.True(link.Length < ReportText.LinkLimit, $"{link.Length} characters");
        Assert.True(link.Length > ReportText.LinkLimit - 40, "only as much is cut as needed");
    }

    [Fact]
    public void ThereIsNoLinkWithoutARepository()
    {
        Assert.Null(ReportText.IssueLink(Crash(), ""));
    }
}
