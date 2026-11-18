using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ClearPlanTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static RunHeader Run(string id, bool pinned = false) => new() { Id = id, Pinned = pinned };

    private static List<RunHeader> Runs(int unpinned, int pinned) =>
        [
            .. Enumerable.Range(1, unpinned).Select(n => Run($"run-u{n}")),
            .. Enumerable.Range(1, pinned).Select(n => Run($"run-p{n}", pinned: true)),
        ];

    private static Comparison Cmp(string a, string b) => ComparisonsFileTests.Sample(a, b);

    [Fact]
    public void UnpinnedRunsAndTheirComparisonsGoPinnedRunsStay()
    {
        var plan = ClearPlan.Of(Runs(37, 4), [Cmp("run-u1", "run-u2"), Cmp("run-u3", "run-u4")], None);
        Assert.Equal(37, plan.Delete.Count);
        Assert.All(plan.Delete, id => Assert.StartsWith("run-u", id));
        Assert.Equal(4, plan.PinnedKept);
        Assert.Equal(["cmp-run-u2", "cmp-run-u4"], plan.ComparisonsDelete);
        Assert.Equal(0, plan.ComparisonsKept);
        Assert.False(plan.Nothing);
    }

    [Fact]
    public void AComparisonWithBothRunsPinnedStays()
    {
        var plan = ClearPlan.Of(Runs(2, 2), [Cmp("run-p1", "run-p2")], None);
        Assert.Empty(plan.ComparisonsDelete);
        Assert.Equal(1, plan.ComparisonsKept);
        Assert.Equal(["run-u1", "run-u2"], plan.Delete);
        Assert.Equal(2, plan.PinnedKept);
    }

    [Theory]
    [InlineData("run-p1", "run-u1")]
    [InlineData("run-u1", "run-p1")]
    public void AComparisonWithOneRunPinnedGoes(string a, string b)
    {
        var plan = ClearPlan.Of(Runs(1, 1), [Cmp(a, b)], None);
        Assert.Equal(["cmp-" + b], plan.ComparisonsDelete);
        Assert.Equal(0, plan.ComparisonsKept);
        // The pinned run itself still stays.
        Assert.Equal(["run-u1"], plan.Delete);
        Assert.Equal(1, plan.PinnedKept);
    }

    [Fact]
    public void AComparisonWhoseRunIsAlreadyGoneGoes()
    {
        var plan = ClearPlan.Of(Runs(0, 1), [Cmp("run-p1", "run-missing")], None);
        Assert.Equal(["cmp-run-missing"], plan.ComparisonsDelete);
        Assert.False(plan.Nothing);
    }

    [Fact]
    public void ARunInProgressIsNeitherDeletedNorCounted()
    {
        var headers = Runs(2, 1);
        headers.Add(Run("run-now"));
        headers.Add(Run("run-now-pinned", pinned: true));
        var plan = ClearPlan.Of(headers, [], new HashSet<string> { "run-now", "run-now-pinned" });
        Assert.Equal(["run-u1", "run-u2"], plan.Delete);
        Assert.Equal(1, plan.PinnedKept);
    }

    [Fact]
    public void AComparisonInProgressAndItsRunsAreInNoneOfTheFields()
    {
        // Side A is saved and unpinned; side B is still being fought.
        var plan = ClearPlan.Of(
            [Run("run-a"), Run("run-u1")],
            [Cmp("run-a", "run-b"), Cmp("run-u1", "run-a")],
            new HashSet<string> { "run-a", "run-b" }
        );
        Assert.Equal(["run-u1"], plan.Delete);
        Assert.Empty(plan.ComparisonsDelete);
        Assert.Equal(0, plan.ComparisonsKept);
        Assert.Equal(0, plan.PinnedKept);
    }

    [Fact]
    public void WithOnlyPinnedRunsAndTheirComparisonsThereIsNothingToClear()
    {
        var plan = ClearPlan.Of(Runs(0, 2), [Cmp("run-p1", "run-p2")], None);
        Assert.True(plan.Nothing);
        Assert.Equal((2, 1), (plan.PinnedKept, plan.ComparisonsKept));
        Assert.True(ClearPlan.Of([], [], None).Nothing);
    }

    [Fact]
    public void AComparisonAloneIsSomethingToClear()
    {
        Assert.False(ClearPlan.Of([], [Cmp("run-x", "run-y")], None).Nothing);
    }
}
