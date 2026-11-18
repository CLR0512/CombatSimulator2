using MelvorMods.CombatSimulator2.Commands.Dev;
using MelvorMods.CombatSimulator2.Engine.Dev;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class BudgetsReportTests
{
    private static readonly DateTime Day = new(2026, 11, 9);

    private static FrameVerdict Take(double worst, int slow, bool pass) => new(worst, slow, 15, 1, pass);

    [Fact]
    public void AFramesLineShowsTheMiddleTakeItsSpreadAndTheControl()
    {
        var line = new FramesLine(
            "open",
            [Take(14.2, 0, true), Take(12.1, 0, true), Take(30.5, 2, false)],
            Noisy: false,
            Usable:
            [
                [34, 36, 32],
                [33, 35, 31],
                [40, 38, 39],
            ],
            UsableLimit: 100
        );
        Assert.True(line.Pass);
        Assert.Equal(
            "worst 14.2 [12.1–30.5] slow 0 [0–2] limit control worst 15 [15–15] slow 1 [1–1] usable 35 [31–40] limit 100 pass",
            line.Text
        );
    }

    [Fact]
    public void AFramesLineFailsWhenMostTakesAreTooSlowToBeUsable()
    {
        var takes = new[] { Take(14, 0, true), Take(14, 0, true), Take(14, 0, true) };
        var slow = new FramesLine(
            "reopen-after-history",
            takes,
            false,
            [
                [150, 160],
                [89, 95],
                [140, 159],
            ],
            100
        );
        Assert.False(slow.Pass);
        // One take's middle repeat over the limit is the game's own pause, not the budget's.
        var once = new FramesLine(
            "open",
            takes,
            false,
            [
                [150, 160, 170],
                [30, 31, 32],
                [30, 31, 32],
            ],
            100
        );
        Assert.True(once.Pass);
        // A take in which nothing became usable has failed.
        var never = new FramesLine(
            "open",
            takes,
            false,
            [
                [],
                [],
                [30],
            ],
            100
        );
        Assert.False(never.Pass);
    }

    [Fact]
    public void AFramesLineSaysNoisyAndWhichLookItIs()
    {
        var line = new FramesLine("scroll-results", [Take(40, 3, false), Take(41, 3, false)], true, Look: "melvui");
        Assert.False(line.Pass);
        Assert.EndsWith(" fail noisy (melvui)", line.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("usable", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueMustHoldInEveryTake()
    {
        Assert.Equal(
            "1850 [1800–1900] MB limit 2048 MB pass",
            new ValueLine("host-peak", [1850, 1900, 1800], 2048, "MB").Text
        );
        var over = new ValueLine("host-peak", [1850, 2100, 1800], 2048, "MB", "fewest copies fighting 4");
        Assert.False(over.Pass);
        Assert.EndsWith("fail (fewest copies fighting 4)", over.Text, StringComparison.Ordinal);
        Assert.True(new ValueLine("closed-alloc", [0, 0, 0], 0, "bytes/frame").Pass);
    }

    [Fact]
    public void AValueWithNoLimitYetIsShownButNeverPasses()
    {
        var line = new ValueLine("host-stock", [1500, 1510], null, "MB");
        Assert.Null(line.Pass);
        Assert.Equal("1500 [1500–1510] MB not measured (no limit yet)", line.Text);
    }

    [Fact]
    public void SmoothnessPassesOnlyWhenEveryWindowDoes()
    {
        var line = new LimitsLine("smoothness", [("run", true), ("warm-up", false), ("idle", true)]);
        Assert.False(line.Pass);
        Assert.Equal("run pass, warm-up fail, idle pass limit control fail", line.Text);
    }

    [Fact]
    public void TheHeaderCountsOnlyWhatWasMeasuredAndFailsOnAnyFail()
    {
        BudgetLine[] lines =
        [
            new ValueLine("closed-alloc", [0], 0, "bytes/frame"),
            new ValueLine("host-peak", [2900], 2048, "MB"),
            new NotMeasuredLine("scroll-results", "the results list isn't shown or has nothing to scroll"),
            new ValueLine("host-stock", [1500], null, "MB"),
            new ElsewhereLine("game-start", "7.1–7.4 s", "startup.sh", "2026-11-09"),
        ];
        var text = BudgetsReport.Render("bench-3", lines, "26316041 0.11.4alpha", Day).Split('\n');
        Assert.Equal("bench-3 fail 1/2 budgets, 2 not measured, game 26316041 0.11.4alpha, 2026-11-09", text[0]);
        Assert.Equal("closed-alloc 0 [0–0] bytes/frame limit 0 bytes/frame pass", text[1]);
        Assert.Equal("scroll-results not measured (the results list isn't shown or has nothing to scroll)", text[3]);
        Assert.Equal("game-start elsewhere 7.1–7.4 s (startup.sh, 2026-11-09)", text[5]);
    }

    [Fact]
    public void NothingMeasuredIsNotAFailButPassesNothing()
    {
        var text = BudgetsReport.Render("bench-1", [new NotMeasuredLine("open", "stopped")], "b", Day);
        Assert.StartsWith("bench-1 pass 0/0 budgets, 1 not measured", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AScriptBudgetShowsItsRecordedValueOrThatItsScriptHasNotRun()
    {
        var recorded = new Budget("run-single", Budgets.Runs, BudgetKind.Reference, Script: "release.sh") with
        {
            Recorded = "0.35–0.44 s",
            RecordedOn = "2026-11-09",
        };
        Assert.Equal("elsewhere 0.35–0.44 s (release.sh, 2026-11-09)", BudgetsReport.Elsewhere(recorded).Text);
        var not = new Budget("game-start", Budgets.Closed, BudgetKind.Reference, Script: "startup.sh");
        Assert.Equal("not measured (run startup.sh)", BudgetsReport.Elsewhere(not).Text);
    }

    [Fact]
    public void TheWorseOfTwoLooksIsKept()
    {
        var pass = new FramesLine("open", [Take(14, 0, true)], false, Look: "base");
        var fail = new FramesLine("open", [Take(40, 3, false)], false, Look: "melvui");
        Assert.Same(fail, BudgetsReport.Worse(pass, fail));
        Assert.Same(fail, BudgetsReport.Worse(fail, pass));
        var worse = new FramesLine("open", [Take(14.9, 0, true)], false, Look: "melvui");
        Assert.Same(worse, BudgetsReport.Worse(pass, worse));
        // A look that couldn't be measured doesn't hide behind the other's pass, and doesn't hide a fail.
        var missing = new NotMeasuredLine("open", "the melvui look can't be shown");
        Assert.Same(missing, BudgetsReport.Worse(pass, missing));
        Assert.Same(fail, BudgetsReport.Worse(missing, fail));
    }

    [Fact]
    public void BudgetsAreChosenByNameOrGroupAndUnknownNamesAreReported()
    {
        Assert.Equal(Budgets.All.Count, Budgets.Select([], out _).Count);
        var chosen = Budgets.Select(["memory", "open", "nonsense"], out var unknown);
        Assert.Equal(["nonsense"], unknown);
        Assert.Equal(
            [
                "open",
                "host-peak",
                "host-peak-low",
                "host-stock",
                "host-idle",
                "host-stopped",
                "copy-bare",
                "copy-fighting",
                "game-added",
            ],
            chosen.Select(b => b.Name)
        );
        Assert.Equal(Budgets.All.Count, Budgets.All.Select(b => b.Name).Distinct().Count());
        Assert.All(Budgets.All, b => Assert.Contains(b.Group, Budgets.Groups));
    }
}
