using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunStorePinTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-pin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static string IdAt(int minute) => TestPaths.RunId(minute);

    [Fact]
    public void PinnedRunsOutliveTheLimit()
    {
        var pinned = new HashSet<string> { IdAt(0), IdAt(1) };
        var store = new RunStore(_dir, 5) { Pinned = () => pinned };
        for (var m = 0; m < 10; m++)
            store.Write(TestPaths.RunAt(m));
        var ids = store.List().Select(h => h.Id).ToList();
        Assert.Equal(7, ids.Count); // the limit plus the two pinned
        Assert.Contains(IdAt(0), ids);
        Assert.Contains(IdAt(1), ids);
        Assert.DoesNotContain(IdAt(2), ids);

        pinned.Clear();
        store.Write(TestPaths.RunAt(10));
        ids = [.. store.List().Select(h => h.Id)];
        Assert.Equal(5, ids.Count);
        Assert.DoesNotContain(IdAt(0), ids);
    }

    [Fact]
    public void ClearKeepsRunsPinnedInTheirHeader()
    {
        var store = new RunStore(_dir, 5);
        for (var m = 0; m < 3; m++)
        {
            var run = TestPaths.RunAt(m);
            run.Pinned = m == 1;
            store.Write(run);
        }
        Assert.Equal(2, store.Clear(ClearPlan.Of(store.List(), [], new HashSet<string>())));
        Assert.Equal([IdAt(1)], store.List().Select(h => h.Id));
    }

    // Five kept comparisons protect ten runs; the third one's runs are as safe as the newest one's, and lose
    // that when their comparison is no longer kept.
    [Fact]
    public void ARunOfAnyKeptComparisonIsNotPrunedUntilItsComparisonIsDropped()
    {
        var kept = new KeptComparisons();
        var store = new RunStore(_dir, 5) { Pinned = () => kept.RunIds };
        for (var n = 0; n < 5; n++)
        {
            store.Write(TestPaths.RunAt(2 * n));
            store.Write(TestPaths.RunAt(2 * n + 1));
            kept.Add(ComparisonsFileTests.Sample(IdAt(2 * n), IdAt(2 * n + 1)));
        }
        for (var m = 10; m < 20; m++)
            store.Write(TestPaths.RunAt(m));
        var ids = store.List().Select(h => h.Id).ToList();
        Assert.Equal(15, ids.Count); // the limit and the ten protected
        Assert.Contains(IdAt(4), ids); // the third comparison's
        Assert.Contains(IdAt(5), ids);
        Assert.NotNull(RunHistory.CanDelete(new RunHeader { Id = IdAt(4) }, null, kept.RunIds));

        // A sixth comparison drops the oldest: its two runs are ordinary runs again, pruned by the limit.
        store.Write(TestPaths.RunAt(20));
        store.Write(TestPaths.RunAt(21));
        var dropped = kept.Add(ComparisonsFileTests.Sample(IdAt(20), IdAt(21)));
        Assert.Equal((IdAt(0), IdAt(1)), (dropped!.A, dropped.B));
        Assert.Null(RunHistory.CanDelete(new RunHeader { Id = IdAt(0) }, null, kept.RunIds));
        store.Write(TestPaths.RunAt(22));
        ids = [.. store.List().Select(h => h.Id)];
        Assert.DoesNotContain(IdAt(0), ids);
        Assert.DoesNotContain(IdAt(1), ids);
        Assert.Contains(IdAt(2), ids);
        Assert.Contains(IdAt(4), ids);
    }

    [Fact]
    public void RunsPinnedInTheirHeaderOutliveTheLimitToo()
    {
        var comparison = new HashSet<string> { IdAt(1) };
        var store = new RunStore(_dir, 5) { Pinned = () => comparison };
        var pinned = TestPaths.RunAt(0);
        pinned.Pinned = true;
        store.Write(pinned);
        for (var m = 1; m < 10; m++)
            store.Write(TestPaths.RunAt(m));
        var ids = store.List().Select(h => h.Id).ToList();
        Assert.Equal(7, ids.Count); // the limit, the run pinned in its header and the comparison's
        Assert.Contains(IdAt(0), ids);
        Assert.Contains(IdAt(1), ids);

        store.Update(IdAt(0), run => run.Pinned = false);
        store.Write(TestPaths.RunAt(10));
        Assert.DoesNotContain(IdAt(0), store.List().Select(h => h.Id));
    }

    [Fact]
    public void TheComparisonsFileIsNotARun()
    {
        var store = new RunStore(_dir, 5);
        store.Write(TestPaths.RunAt(0));
        ComparisonsFile.Write(_dir, [ComparisonsFileTests.Sample()]);
        Assert.Single(store.List());
        Assert.Empty(store.Skipped);
    }

    [Fact]
    public void ARunsCharacterIsReadFromTheStartOfItsFile()
    {
        var store = new RunStore(_dir, 5);
        var run = TestPaths.RunAt(0);
        store.Write(run);
        Assert.Equal(run.Setup.Character, store.CharacterOf(IdAt(0)));
        Assert.False(string.IsNullOrEmpty(run.Setup.Character));
        Assert.Null(store.CharacterOf(IdAt(1)));
        File.WriteAllText(Path.Combine(_dir, IdAt(2) + RunFile.Extension), "not gzip");
        Assert.Null(store.CharacterOf(IdAt(2)));
        // A run recorded in the game, with its full results after the setup.
        Assert.Equal(
            "Dev",
            RunFile.ReadCharacter(TestPaths.Fixtures(Path.Combine("probe", "run-20261111-145148-1.json.gz")))
        );
    }
}
