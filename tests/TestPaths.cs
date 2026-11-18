using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>Paths and run ids the tests share: the mod's folder, and sample runs a minute apart.</summary>
internal static class TestPaths
{
    /// <summary>The mod's folder, which holds its sources and these tests, as the build recorded it.</summary>
    public static string ModFolder()
    {
        var folder = typeof(TestPaths)
            .Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ModFolder")
            ?.Value;
        Assert.False(string.IsNullOrEmpty(folder), "the build didn't record the mod's folder");
        return folder!;
    }

    /// <summary>The tests' fixture files, beside the tests.</summary>
    public static string Fixtures(string name) => Path.Combine(ModFolder(), "tests", "Fixtures", name);

    /// <summary>
    /// The <c>.editorconfig</c> that governs the mod's sources: the nearest one at or above its folder, which is
    /// the folder itself where the mod is a repository of its own.
    /// </summary>
    public static string EditorConfig()
    {
        for (var folder = new DirectoryInfo(ModFolder()); folder != null; folder = folder.Parent)
        {
            var path = Path.Combine(folder.FullName, ".editorconfig");
            if (File.Exists(path))
                return path;
        }
        Assert.Fail("no .editorconfig at or above the mod's folder");
        return "";
    }

    /// <summary>The id of the sample run started at 12:<paramref name="minute"/> on 6 November 2026.</summary>
    public static string RunId(int minute) => $"run-20261106-12{minute:00}00-1";

    /// <summary>A sample run with that id, started at that minute.</summary>
    public static Run RunAt(int minute) =>
        ResultsModelTests.SampleRun(RunId(minute), new DateTime(2026, 11, 6, 12, minute, 0, DateTimeKind.Utc));
}
