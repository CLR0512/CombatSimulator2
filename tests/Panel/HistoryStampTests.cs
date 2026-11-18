using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Settings;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class HistoryStampTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-stamp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private RunStore Store()
    {
        var store = new RunStore(_dir, 20);
        store.Write(TestPaths.RunAt(0));
        store.Write(TestPaths.RunAt(1));
        return store;
    }

    [Fact]
    public void ReadingTheStoreLeavesTheRowsAsTheyAre()
    {
        var store = Store();
        var built = HistoryStamp.Of(store, expanded: true);
        store.List();
        store.ListAll();
        store.Read(TestPaths.RunId(0));
        store.Newest();
        Assert.Equal(built, HistoryStamp.Of(store, expanded: true));
    }

    [Fact]
    public void ASavedRenamedPinnedOrDeletedRunMakesTheRowsStale()
    {
        var store = Store();
        var built = HistoryStamp.Of(store, true);
        store.Write(TestPaths.RunAt(2));
        Assert.NotEqual(built, built = HistoryStamp.Of(store, true));
        store.Update(TestPaths.RunId(0), run => run.Name = "kept");
        Assert.NotEqual(built, built = HistoryStamp.Of(store, true));
        store.Update(TestPaths.RunId(0), run => run.Pinned = true);
        Assert.NotEqual(built, built = HistoryStamp.Of(store, true));
        Assert.True(store.Delete(TestPaths.RunId(1)));
        Assert.NotEqual(built, built = HistoryStamp.Of(store, true));
        store.Clear(ClearPlan.Of(store.List(), [], new HashSet<string>()));
        Assert.NotEqual(built, HistoryStamp.Of(store, true));
    }

    [Fact]
    public void DeletingARunThatIsNotThereChangesNothing()
    {
        var store = Store();
        var built = HistoryStamp.Of(store, true);
        Assert.False(store.Delete(TestPaths.RunId(9)));
        Assert.Equal(built, HistoryStamp.Of(store, true));
    }

    [Fact]
    public void TheKeepLimitTheComparisonsRunsAndOpeningTheListCount()
    {
        var store = Store();
        var held = new HashSet<string>();
        store.Pinned = () => held;
        var built = HistoryStamp.Of(store, false);
        Assert.NotEqual(built, HistoryStamp.Of(store, true));
        store.Max = 50;
        Assert.NotEqual(built, built = HistoryStamp.Of(store, false));
        held.Add(TestPaths.RunId(0));
        Assert.NotEqual(built, HistoryStamp.Of(store, false));
    }
}
