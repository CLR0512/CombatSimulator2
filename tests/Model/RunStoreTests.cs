using System.IO.Compression;
using System.Text;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Fact]
    public void The21stRunRemovesTheOldest()
    {
        var store = new RunStore(_dir, 20);
        // Written out of order: the oldest by end time goes, not the first written.
        store.Write(TestPaths.RunAt(30));
        for (var m = 0; m < 20; m++)
            store.Write(TestPaths.RunAt(m == 5 ? 59 : m));
        var ids = store.List().Select(h => h.Id).ToList();
        Assert.Equal(20, ids.Count);
        Assert.DoesNotContain("run-20261106-120000-1", ids);
        Assert.Contains("run-20261106-123000-1", ids);
        Assert.Equal(20, Directory.GetFiles(_dir, "*" + RunFile.Extension).Length);
    }

    [Fact]
    public void ListIsNewestFirstAndReadsHeadersOnly()
    {
        var store = new RunStore(_dir, 20);
        store.Write(TestPaths.RunAt(1));
        store.Write(TestPaths.RunAt(2));
        // A file whose header is intact but whose run is cut off: listing still works, reading fails.
        var header = RunFile.HeaderJson(TestPaths.RunAt(3));
        WriteGzip(
            Path.Combine(_dir, "run-20261106-120300-1" + RunFile.Extension),
            $"{{\"format\":1,\"header\":{header},\"run\":{{\"id\":"
        );
        var list = store.List();
        Assert.Equal(
            ["run-20261106-120300-1", "run-20261106-120200-1", "run-20261106-120100-1"],
            list.Select(h => h.Id)
        );
        Assert.Equal(2, list[0].Targets);
        Assert.Throws<RunFileException>(() => store.Read("run-20261106-120300-1"));
        Assert.Equal("run-20261106-120200-1", store.Read("run-20261106-120200-1").Id);
    }

    [Fact]
    public void UnreadableAndNewerFilesAreSkippedAndReported()
    {
        var store = new RunStore(_dir, 20);
        store.Write(TestPaths.RunAt(1));
        File.WriteAllText(Path.Combine(_dir, "run-broken" + RunFile.Extension), "not gzip");
        WriteGzip(Path.Combine(_dir, "run-future" + RunFile.Extension), "{\"format\":4,\"header\":{},\"run\":{}}");
        var list = store.List();
        Assert.Single(list);
        Assert.Equal(2, store.Skipped.Count);
        Assert.Contains(
            store.Skipped,
            s => s.Contains("run-future", StringComparison.Ordinal) && s.Contains("format 4", StringComparison.Ordinal)
        );
        Assert.Contains(store.Skipped, s => s.Contains("run-broken", StringComparison.Ordinal));
    }

    [Fact]
    public void NewestReadsTheLatestRun()
    {
        var store = new RunStore(_dir, 20);
        Assert.Null(store.Newest());
        store.Write(TestPaths.RunAt(4));
        store.Write(TestPaths.RunAt(9));
        Assert.Equal("run-20261106-120900-1", store.Newest()!.Id);
    }

    [Fact]
    public void TheLimitCanChange()
    {
        var store = new RunStore(_dir, 20);
        for (var m = 0; m < 8; m++)
            store.Write(TestPaths.RunAt(m));
        store.Max = 5;
        Assert.Equal(8, store.List().Count);
        store.Write(TestPaths.RunAt(30));
        var ids = store.List().Select(h => h.Id).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Equal("run-20261106-123000-1", ids[0]);
        Assert.DoesNotContain("run-20261106-120200-1", ids);
        store.Max = 1000;
        Assert.Equal(RunStore.MaxLimit, store.Max);
        store.Max = 0;
        Assert.Equal(RunStore.MinLimit, store.Max);
    }

    private static ClearPlan PlanFor(RunStore store, params string[] inProgress) =>
        ClearPlan.Of(store.List(), [], inProgress.ToHashSet());

    [Fact]
    public void ClearDeletesEverySavedRunAndUnreadableFile()
    {
        var store = new RunStore(_dir, 20);
        store.Write(TestPaths.RunAt(1));
        store.Write(TestPaths.RunAt(2));
        File.WriteAllText(Path.Combine(_dir, "run-broken" + RunFile.Extension), "not gzip");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "kept");
        Assert.Equal(3, store.Clear(PlanFor(store)));
        Assert.Empty(store.List());
        Assert.Equal(0, store.SizeOnDisk);
        Assert.True(File.Exists(Path.Combine(_dir, "notes.txt")));
        var missing = new RunStore(Path.Combine(_dir, "missing"));
        Assert.Equal(0, missing.Clear(PlanFor(missing)));
    }

    // The files left are exactly the ones the plan didn't name: pinned runs, the run in progress, a run saved
    // after the plan was made, and everything that isn't a run.
    [Fact]
    public void TheFilesLeftAfterAClearEqualThePlan()
    {
        var store = new RunStore(_dir, 20);
        for (var m = 1; m <= 6; m++)
        {
            var run = TestPaths.RunAt(m);
            run.Pinned = m is 2 or 5;
            store.Write(run);
        }
        ComparisonsFile.Write(_dir, [ComparisonsFileTests.Sample(TestPaths.RunId(1), TestPaths.RunId(3))]);
        var plan = PlanFor(store, TestPaths.RunId(4));
        Assert.Equal([TestPaths.RunId(1), TestPaths.RunId(3), TestPaths.RunId(6)], plan.Delete.Order());
        Assert.Equal(2, plan.PinnedKept);
        store.Write(TestPaths.RunAt(7)); // finished between the two clicks

        var changes = store.Changes;
        Assert.Equal(3, store.Clear(plan));
        Assert.Equal(
            [TestPaths.RunId(7), TestPaths.RunId(5), TestPaths.RunId(4), TestPaths.RunId(2)],
            store.List().Select(h => h.Id)
        );
        Assert.True(store.Changes > changes);
        // The store deletes runs only: the kept comparisons are their keeper's.
        Assert.True(File.Exists(ComparisonsFile.PathIn(_dir)));
    }

    [Fact]
    public void SizeOnDiskSumsTheRunFiles()
    {
        var store = new RunStore(_dir, 20);
        Assert.Equal(0, store.SizeOnDisk);
        store.Write(TestPaths.RunAt(1));
        store.Write(TestPaths.RunAt(2));
        var expected = Directory.GetFiles(_dir, "*" + RunFile.Extension).Sum(f => new FileInfo(f).Length);
        Assert.True(expected > 0);
        Assert.Equal(expected, store.SizeOnDisk);
    }

    [Fact]
    public void UpdateChangesNameAndPinAndKeepsTheOrder()
    {
        var store = new RunStore(_dir, 20);
        for (var m = 0; m < 3; m++)
            store.Write(TestPaths.RunAt(m));
        var before = store.List().Select(h => h.Id).ToList();
        var header = store.Update(
            "run-20261106-120000-1",
            run =>
            {
                run.Name = "oldest";
                run.Pinned = true;
                run.EndedAt = DateTime.UtcNow; // a change can't move the run in the list
            }
        );
        Assert.Equal("oldest", header.Name);
        Assert.True(header.Pinned);
        Assert.Equal(before, store.List().Select(h => h.Id));
        var read = store.Read("run-20261106-120000-1");
        Assert.Equal("oldest", read.Name);
        Assert.True(read.Pinned);
        Assert.Equal(new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc), read.EndedAt);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Throws<RunFileException>(() => store.Update("run-missing", _ => { }));
    }

    [Fact]
    public void DeleteRemovesOneFile()
    {
        var store = new RunStore(_dir, 20);
        store.Write(TestPaths.RunAt(1));
        store.Write(TestPaths.RunAt(2));
        Assert.True(store.Delete("run-20261106-120100-1"));
        Assert.False(store.Delete("run-20261106-120100-1"));
        Assert.Equal(["run-20261106-120200-1"], store.List().Select(h => h.Id));
        // An unreadable file goes by its file name; nothing outside the folder can be named.
        File.WriteAllText(Path.Combine(_dir, "run-broken" + RunFile.Extension), "not gzip");
        Assert.True(store.Delete("run-broken" + RunFile.Extension));
        Assert.Throws<ArgumentException>(() => store.Delete("../run-20261106-120200-1"));
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "kept");
        Assert.False(store.Delete("notes.txt")); // names notes.txt.json.gz, which isn't there
        Assert.True(File.Exists(Path.Combine(_dir, "notes.txt")));
    }

    [Fact]
    public void ListAllGivesUnreadableFilesWithWhy()
    {
        var store = new RunStore(_dir, 20);
        store.Write(TestPaths.RunAt(1));
        File.WriteAllText(Path.Combine(_dir, "run-broken" + RunFile.Extension), "not gzip");
        var (headers, unreadable) = store.ListAll();
        Assert.Equal(["run-20261106-120100-1"], headers.Select(h => h.Id));
        var bad = Assert.Single(unreadable);
        Assert.Equal("run-broken" + RunFile.Extension, bad.File);
        Assert.Contains("unreadable", bad.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreActionsStayQuickWithAHundredRuns()
    {
        var store = new RunStore(_dir, RunStore.MaxLimit);
        var run = TestPaths.RunAt(0);
        for (var i = 0; i < 100; i++)
        {
            run.Id = $"run-20261106-12{i / 60:00}{i % 60:00}-1";
            run.EndedAt = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc).AddSeconds(i);
            store.Write(run);
        }
        Assert.Equal(100, store.List().Count);
        foreach (
            var (name, action) in new (string, Action)[]
            {
                ("list", () => store.List()),
                ("list all", () => store.ListAll()),
                ("update", () => store.Update("run-20261106-120030-1", r => r.Name = "named")),
                ("delete", () => store.Delete("run-20261106-120031-1")),
            }
        )
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            action();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"{name} took {watch.Elapsed.TotalMilliseconds:0} ms");
        }
    }

    private static void WriteGzip(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(Encoding.UTF8.GetBytes(text));
    }
}
