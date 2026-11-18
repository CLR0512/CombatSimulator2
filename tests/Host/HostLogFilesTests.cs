using MelvorMods.CombatSimulator2.Host;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class HostLogFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cs2-logs-" + Guid.NewGuid().ToString("N"));

    public HostLogFilesTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_folder, name), text);

    private string Read(string name) => File.ReadAllText(Path.Combine(_folder, name));

    private bool Exists(string name) => File.Exists(Path.Combine(_folder, name));

    [Fact]
    public void AfterAnOrdinaryStopBothLogsBecomeThePreviousOnes()
    {
        Write(HostLogFiles.HostLog, "host");
        Write(HostLogFiles.GodotLog, "godot");

        HostLogFiles.Keep(_folder, crashed: false, _ => Assert.Fail("nothing to warn about"));

        Assert.Equal("host", Read("CombatSimulator2.host.previous.log"));
        Assert.Equal("godot", Read("CombatSimulator2.host.godot.previous.log"));
        Assert.False(Exists(HostLogFiles.HostLog));
        Assert.False(Exists(HostLogFiles.GodotLog));
    }

    [Fact]
    public void AfterACrashTheLogsGoToTheCrashCopiesAndTheNextStartLeavesThem()
    {
        Write(HostLogFiles.HostLog, "crashed host");
        HostLogFiles.Keep(_folder, crashed: true, _ => { });
        Write(HostLogFiles.HostLog, "next host");
        HostLogFiles.Keep(_folder, crashed: false, _ => { });

        Assert.Equal("crashed host", Read("CombatSimulator2.host.crash.log"));
        Assert.Equal("next host", Read("CombatSimulator2.host.previous.log"));
    }

    [Fact]
    public void ANewerCopyReplacesTheOlderOne()
    {
        Write("CombatSimulator2.host.crash.log", "older crash");
        Write(HostLogFiles.HostLog, "newer crash");

        HostLogFiles.Keep(_folder, crashed: true, _ => { });

        Assert.Equal("newer crash", Read("CombatSimulator2.host.crash.log"));
    }

    [Fact]
    public void ALogStillOpenIsWaitedForThenReported()
    {
        Write(HostLogFiles.GodotLog, "held");
        var warnings = new List<string>();
        using (
            new FileStream(Path.Combine(_folder, HostLogFiles.GodotLog), FileMode.Open, FileAccess.Read, FileShare.Read)
        )
            HostLogFiles.Keep(_folder, crashed: false, warnings.Add, TimeSpan.FromMilliseconds(300));

        Assert.Single(warnings);
        Assert.StartsWith(HostLogFiles.GodotLog + " not kept", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ALogLetGoOfWhileWaitingIsKept()
    {
        Write(HostLogFiles.GodotLog, "held");
        var held = new FileStream(
            Path.Combine(_folder, HostLogFiles.GodotLog),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        var release = new Thread(() =>
        {
            Thread.Sleep(300);
            held.Dispose();
        });
        release.Start();
        HostLogFiles.Keep(_folder, crashed: false, _ => Assert.Fail("it was let go of in time"));
        release.Join();

        Assert.Equal("held", Read("CombatSimulator2.host.godot.previous.log"));
    }

    [Fact]
    public void NoLogsMeansNothingToDo()
    {
        HostLogFiles.Keep(_folder, crashed: true, _ => Assert.Fail("nothing to warn about"));
        Assert.Empty(Directory.GetFiles(_folder));
    }
}
