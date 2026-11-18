using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class HostDumpsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cs2-dumps-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _start = new(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc);

    public HostDumpsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void Dump(string name, int bytes, int minutes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, _start.AddMinutes(minutes));
    }

    [Theory]
    [InlineData("CombatSimulator2.host.40152.1794254681.dmp", 40152, false)]
    [InlineData("CombatSimulator2.host.80616.1794255087.hang.dmp", 80616, true)]
    public void ANameGivesItsPidAndKind(string name, int pid, bool hang)
    {
        var parsed = HostDumpInfo.Parse(name);
        Assert.Equal((pid, hang ? DumpKind.Hang : DumpKind.Crash), parsed);
    }

    [Theory]
    [InlineData("CombatSimulator2.host.log")]
    [InlineData("Other.host.1.2.dmp")]
    [InlineData("CombatSimulator2.host.x.2.dmp")]
    [InlineData("CombatSimulator2.host.1.2.crash.dmp")]
    public void OtherFilesAreNotDumps(string name) => Assert.Null(HostDumpInfo.Parse(name));

    [Fact]
    public void AHangDumpIsNamedSoItParsesBack()
    {
        var name = HostDumpInfo.HangName(4242, _start);
        Assert.Equal("CombatSimulator2.host.4242.1794229200.hang.dmp", name);
        Assert.Equal((4242, DumpKind.Hang), HostDumpInfo.Parse(name));
    }

    [Fact]
    public void TheRuntimesNameIsQuotedForAFolderWithSpaces()
    {
        var root = Path.Combine(_folder, "Melvor Idle 2", "mods", "CombatSimulator2");
        var environment = new Dictionary<string, string>();
        HostDumps.AddEnvironment(environment, root);

        Assert.Equal("1", environment["DOTNET_DbgEnableMiniDump"]);
        Assert.Equal("3", environment["DOTNET_DbgMiniDumpType"]);
        var name = environment["DOTNET_DbgMiniDumpName"];
        Assert.StartsWith("\"", name, StringComparison.Ordinal);
        Assert.EndsWith("CombatSimulator2.host.%p.%t.dmp\"", name, StringComparison.Ordinal);
        Assert.True(Directory.Exists(HostDumps.Folder(root)));
    }

    [Fact]
    public void PruningKeepsTheNewestThree()
    {
        for (var i = 0; i < 5; i++)
            Dump($"CombatSimulator2.host.{100 + i}.{i}.dmp", 10, i);

        var removed = HostDumps.Prune(_folder, keepBytes: long.MaxValue);

        Assert.Equal(["CombatSimulator2.host.101.1.dmp", "CombatSimulator2.host.100.0.dmp"], removed);
        Assert.Equal([104, 103, 102], HostDumps.List(_folder).Select(d => d.Pid));
    }

    [Fact]
    public void PruningKeepsTheTotalUnderItsLimitOldestGoingFirst()
    {
        Dump("CombatSimulator2.host.1.1.dmp", 600, 1);
        Dump("CombatSimulator2.host.2.2.hang.dmp", 300, 2);
        Dump("CombatSimulator2.host.3.3.dmp", 500, 3);

        HostDumps.Prune(_folder, keepCount: 3, keepBytes: 1000);

        Assert.Equal([3, 2], HostDumps.List(_folder).Select(d => d.Pid));
    }

    [Fact]
    public void ACrashDumpCountsOnlyForItsPidAndSinceTheHostStarted()
    {
        Dump("CombatSimulator2.host.7.1.dmp", 100, 5);
        Dump("CombatSimulator2.host.7.0.dmp", 50, -5);
        Dump("CombatSimulator2.host.7.2.hang.dmp", 70, 5);
        Dump("CombatSimulator2.host.8.1.dmp", 30, 5);

        Assert.Equal(100, HostDumps.CrashDumpBytes(_folder, 7, _start));
        Assert.Null(HostDumps.CrashDumpBytes(_folder, 9, _start));
        Assert.Null(HostDumps.CrashDumpBytes(Path.Combine(_folder, "none"), 7, _start));
    }
}
