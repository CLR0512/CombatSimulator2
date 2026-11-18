using System.IO.Compression;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ReportWriterTests : IDisposable
{
    private static readonly RedactionNames Alexandra = new(@"C:\Users\Alexandra", "Alexandra", "ALEX-DESKTOP");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cs2-report-" + Guid.NewGuid().ToString("N"));
    private readonly string _fixtures;

    public ReportWriterTests()
    {
        _fixtures = Path.Combine(_root, "fixtures");
        CopyFolder(TestPaths.Fixtures("report"), _fixtures);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.GetDirectories(from))
            CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
    }

    private string Fixture(string name) => File.ReadAllText(Path.Combine(_fixtures, name));

    private string Reports => Path.Combine(_root, "reports");

    private ReportContents Contents(
        string file = "CombatSimulator2-report-20261109-140312.zip",
        long logBound = 1 << 20
    )
    {
        var dump = new HostDumpInfo(
            "CombatSimulator2.host.4242.1794229200.dmp",
            4242,
            DateTime.UtcNow,
            1024,
            DumpKind.Crash
        );
        return new ReportContents(
            ReportTextTests.Crash() with
            {
                ReportFile = file,
            },
            """{ "cs2Version": "0.1.0", "machine": "ALEX-DESKTOP" }""",
            Fixture("settings.json"),
            Fixture("runs.json"),
            Fixture("failures.json"),
            [
                new ReportLog(Path.Combine(_fixtures, "CombatSimulator2.log"), "logs/CombatSimulator2.log", logBound),
                new ReportLog(
                    Path.Combine(_fixtures, "CombatSimulator2.previous.log"),
                    "logs/CombatSimulator2.previous.log",
                    logBound
                ),
                new ReportLog(
                    Path.Combine(_fixtures, "CombatSimulator2.host.log"),
                    "logs/CombatSimulator2.host.log",
                    logBound
                ),
                new ReportLog(Path.Combine(_fixtures, "missing.log"), "logs/missing.log", logBound),
            ],
            "host 4242: no entry (not found)\n",
            _fixtures,
            [dump],
            Searches: """[ { "kind": "gear", "target": "MelvorBase:GolbinForest/MelvorBase:Golbin" } ]"""
        );
    }

    private static Dictionary<string, string> Texts(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive
            .Entries.Where(e => !e.FullName.EndsWith(".dmp", StringComparison.Ordinal))
            .ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
    }

    [Fact]
    public void TheZipHoldsTheContractsEntriesAndLogsThatExist()
    {
        var result = ReportWriter.Write(Contents(), Reports, Alexandra);

        Assert.Equal(
            [
                "summary.txt",
                "about.json",
                "settings.json",
                "runs.json",
                "failures.json",
                "searches.json",
                "logs/CombatSimulator2.log",
                "logs/CombatSimulator2.previous.log",
                "logs/CombatSimulator2.host.log",
                "eventlog.txt",
                "dumps.txt",
                "snapshots.txt",
                "dumps/CombatSimulator2.host.4242.1794229200.dmp",
            ],
            result.Entries
        );
        using var archive = ZipFile.OpenRead(Path.Combine(Reports, result.File));
        Assert.Equal(result.Entries, archive.Entries.Select(e => e.FullName));
        Assert.True(result.Bytes > 0);
    }

    [Fact]
    public void EveryTextEntryIsRedacted()
    {
        var result = ReportWriter.Write(Contents(), Reports, Alexandra);
        var texts = Texts(Path.Combine(Reports, result.File));

        foreach (var (name, text) in texts)
        {
            Assert.DoesNotContain("alexandra", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("alex-desktop", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains(@"<user>\AppData\Roaming", texts["logs/CombatSimulator2.log"], StringComparison.Ordinal);
        Assert.Contains("<machine>", texts["about.json"], StringComparison.Ordinal);
        Assert.Contains("saved by <user>", texts["settings.json"], StringComparison.Ordinal);
    }

    [Fact]
    public void TheSummaryHandedBackIsRedactedForTheTextsBuiltFromIt()
    {
        var contents = Contents() with
        {
            Summary = ReportTextTests.Crash(@"host crash: C:\Users\Alexandra\x on ALEX-DESKTOP"),
        };
        var result = ReportWriter.Write(contents, Reports, Alexandra);

        Assert.Equal(@"host crash: <user>\x on <machine>", result.Summary.LastError);
        Assert.DoesNotContain("Alexandra", ReportText.Discord(result.Summary), StringComparison.Ordinal);
    }

    [Fact]
    public void ALogGoesInAsItsEndWithinItsBoundFromAWholeLine()
    {
        var result = ReportWriter.Write(Contents(logBound: 120), Reports, Alexandra);
        var log = Texts(Path.Combine(Reports, result.File))["logs/CombatSimulator2.log"];

        Assert.True(log.Length <= 120, $"{log.Length} characters");
        Assert.StartsWith("13:20:39.003", log, StringComparison.Ordinal);
    }

    [Fact]
    public void ADumpThatWouldPassTheLimitIsLeftOutAndSaidSo()
    {
        var contents = Contents();
        var result = ReportWriter.Write(contents, Reports, Alexandra, sizeLimit: 1200);

        Assert.DoesNotContain(result.Entries, e => e.EndsWith(".dmp", StringComparison.Ordinal));
        var dumps = Texts(Path.Combine(Reports, result.File))["dumps.txt"];
        Assert.Contains("CombatSimulator2.host.4242.1794229200.dmp  crash  1 KB", dumps, StringComparison.Ordinal);
        Assert.Contains("not in this zip", dumps, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNewestTenReportsAreKept()
    {
        for (var i = 0; i < 12; i++)
            ReportWriter.Write(Contents($"CombatSimulator2-report-20261109-1400{i:00}.zip"), Reports, Alexandra);

        var kept = Directory.GetFiles(Reports).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(10, kept.Count);
        Assert.Equal("CombatSimulator2-report-20261109-140002.zip", kept[0]);
        Assert.Equal("CombatSimulator2-report-20261109-140011.zip", kept[^1]);
    }

    [Fact]
    public void NoSavedRunGoesInAndTheSaveOnlyWhenGiven()
    {
        var without = ReportWriter.Write(Contents("CombatSimulator2-report-20261109-140001.zip"), Reports, Alexandra);
        var save = (Name: "Dev.save", Bytes: new byte[] { 1, 2, 3 });
        var with = ReportWriter.Write(
            Contents("CombatSimulator2-report-20261109-140002.zip") with
            {
                Save = save,
            },
            Reports,
            Alexandra
        );

        Assert.DoesNotContain(without.Entries, e => e.EndsWith(".json.gz", StringComparison.Ordinal));
        Assert.DoesNotContain(without.Entries, e => e.StartsWith("save/", StringComparison.Ordinal));
        Assert.Contains("save/Dev.save", with.Entries);
        using var archive = ZipFile.OpenRead(Path.Combine(Reports, with.File));
        using var stream = new MemoryStream();
        archive.GetEntry("save/Dev.save")!.Open().CopyTo(stream);
        Assert.Equal(save.Bytes, stream.ToArray());
    }

    [Fact]
    public void ReportsPastTheSpaceGoOldestFirstButTheNewOneStays()
    {
        var first = ReportWriter.Write(Contents("CombatSimulator2-report-20261109-140001.zip"), Reports, Alexandra);
        var bytes = first.Bytes;
        ReportWriter.Write(Contents("CombatSimulator2-report-20261109-140002.zip"), Reports, Alexandra);
        ReportWriter.Write(
            Contents("CombatSimulator2-report-20261109-140003.zip"),
            Reports,
            Alexandra,
            keepBytes: bytes * 2 + bytes / 2
        );

        Assert.Equal(
            ["CombatSimulator2-report-20261109-140002.zip", "CombatSimulator2-report-20261109-140003.zip"],
            Directory.GetFiles(Reports).Select(Path.GetFileName).Order(StringComparer.Ordinal)
        );
        ReportWriter.Write(Contents("CombatSimulator2-report-20261109-140004.zip"), Reports, Alexandra, keepBytes: 1);
        Assert.Equal(
            ["CombatSimulator2-report-20261109-140004.zip"],
            Directory.GetFiles(Reports).Select(Path.GetFileName)
        );
    }

    private ReportSnapshot Snapshot(string id, int minute, int bytes)
    {
        var path = Path.Combine(_root, "snapshots", id + ".save");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = new byte[bytes];
        new Random(minute).NextBytes(content);
        File.WriteAllBytes(path, content);
        return new ReportSnapshot(id, "Dev", "sha-" + id, bytes, new DateTime(2026, 11, 13, 10, minute, 0), path);
    }

    private ReportContents WithSnapshots(bool save, params ReportSnapshot[] snapshots) =>
        Contents() with
        {
            Save = save ? ("Dev.save", new byte[] { 1, 2, 3 }) : null,
            Snapshots = snapshots,
        };

    [Fact]
    public void SnapshotsGoInNewestFirstWithTheSave()
    {
        var result = ReportWriter.Write(
            WithSnapshots(true, Snapshot("run-2", 2, 100), Snapshot("run-1", 1, 100)),
            Reports,
            Alexandra
        );

        Assert.Equal(
            ["snapshots/run-2.save", "snapshots/run-1.save"],
            result.Entries.Where(e => e.StartsWith("snapshots/", StringComparison.Ordinal))
        );
        var list = Texts(Path.Combine(Reports, result.File))["snapshots.txt"];
        Assert.Contains("run-2  Dev  sha256 sha-run-2", list, StringComparison.Ordinal);
        Assert.Contains("in this zip", list, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheSaveNoSnapshotGoesIn()
    {
        var result = ReportWriter.Write(WithSnapshots(false, Snapshot("run-1", 1, 100)), Reports, Alexandra);

        Assert.DoesNotContain(result.Entries, e => e.StartsWith("snapshots/", StringComparison.Ordinal));
        Assert.Contains(
            "left out by choice",
            Texts(Path.Combine(Reports, result.File))["snapshots.txt"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ASnapshotGoesBeforeTheDumpAndTheOldestFirstOut()
    {
        var newest = Snapshot("run-2", 2, 8000);
        var oldest = Snapshot("run-1", 1, 8000);
        var one = ReportWriter.Write(
            WithSnapshots(true, newest) with
            {
                Dumps = [],
            },
            Path.Combine(_root, "measure"),
            Alexandra
        );

        var result = ReportWriter.Write(
            WithSnapshots(true, newest, oldest),
            Reports,
            Alexandra,
            sizeLimit: one.Bytes + 300
        );

        Assert.Contains("snapshots/run-2.save", result.Entries);
        Assert.DoesNotContain("snapshots/run-1.save", result.Entries);
        Assert.DoesNotContain(result.Entries, e => e.EndsWith(".dmp", StringComparison.Ordinal));
        Assert.Contains(
            "run-1  Dev  sha256 sha-run-1  7 KB  2026-11-13 10:01:00 UTC  not in this zip: over the size limit",
            Texts(Path.Combine(Reports, result.File))["snapshots.txt"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void WhatThePanelShowedGoesInRedacted()
    {
        var result = ReportWriter.Write(
            Contents() with
            {
                Panel = "view=simulate\nside A <loadout 2>\nfolder C:\\Users\\Alexandra\\x",
            },
            Reports,
            Alexandra
        );

        var panel = Texts(Path.Combine(Reports, result.File))["panel.txt"];
        Assert.Contains("side A <loadout 2>", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Alexandra", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAreNamedByLocalTime() =>
        Assert.Equal(
            "CombatSimulator2-report-20261109-140312.zip",
            ReportWriter.NameFor(new DateTime(2026, 11, 9, 14, 3, 12, DateTimeKind.Local))
        );
}
