using MelvorMods.Core;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class WorkerModeTests
{
    private static readonly string[] Godot =
    [
        @"C:\Games\Melvor Idle 2\Melvor2.exe",
        "--headless",
        "--max-fps",
        "10",
        "--log-file",
        @"C:\Games\Melvor Idle 2\CombatSimulator2\logs\CombatSimulator2.host.godot.log",
        "--",
    ];

    private static string[] Line(params string[] user) => [.. Godot, .. user];

    [Fact]
    public void AFullCommandLineIsAWorker()
    {
        var args = WorkerArgs.Parse(
            Line(
                "--melvormods-worker=CombatSimulator2:host",
                "--melvormods-pipe=MelvorMods.CombatSimulator2.host.1234.ab12",
                "--melvormods-parent=1234"
            ),
            out var problem
        );

        Assert.Null(problem);
        Assert.Equal(
            new WorkerArgs("CombatSimulator2", "host", "MelvorMods.CombatSimulator2.host.1234.ab12", 1234),
            args
        );
    }

    [Fact]
    public void GodotFlagsAloneAreNotAWorkerAndNotAProblem()
    {
        Assert.Null(WorkerArgs.Parse(Godot, out var problem));
        Assert.Null(problem);
        Assert.Null(WorkerArgs.Parse([@"C:\Games\Melvor2.exe"], out problem));
        Assert.Null(problem);
        Assert.Null(WorkerArgs.Parse([], out problem));
        Assert.Null(problem);
    }

    [Fact]
    public void FlagsAreFoundInAnyOrder()
    {
        var args = WorkerArgs.Parse(
            Line("--melvormods-parent=7", "--melvormods-pipe=p", "--melvormods-worker=Mod:role"),
            out var problem
        );

        Assert.Null(problem);
        Assert.Equal(new WorkerArgs("Mod", "role", "p", 7), args);
    }

    [Theory]
    [InlineData("--melvormods-worker=CombatSimulator2", "worker")]
    [InlineData("--melvormods-worker=:host", "worker")]
    [InlineData("--melvormods-worker=CombatSimulator2:", "worker")]
    [InlineData("--melvormods-worker=Combat Simulator:host", "worker")]
    [InlineData("--melvormods-worker=a.b:host", "worker")]
    [InlineData("--melvormods-worker=a:b:c", "worker")]
    public void AMalformedWorkerValueIsNotAWorker(string flag, string named)
    {
        var args = WorkerArgs.Parse(Line(flag, "--melvormods-pipe=p", "--melvormods-parent=1"), out var problem);

        Assert.Null(args);
        Assert.NotNull(problem);
        Assert.Contains(named, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "--melvormods-parent=1", "pipe")]
    [InlineData("--melvormods-pipe=", "--melvormods-parent=1", "pipe")]
    [InlineData("--melvormods-pipe=a b", "--melvormods-parent=1", "pipe")]
    [InlineData("--melvormods-pipe=a\\b", "--melvormods-parent=1", "pipe")]
    [InlineData("--melvormods-pipe=p", null, "parent")]
    [InlineData("--melvormods-pipe=p", "--melvormods-parent=", "parent")]
    [InlineData("--melvormods-pipe=p", "--melvormods-parent=abc", "parent")]
    [InlineData("--melvormods-pipe=p", "--melvormods-parent=0", "parent")]
    [InlineData("--melvormods-pipe=p", "--melvormods-parent=-5", "parent")]
    public void AMissingOrMalformedPipeOrParentIsNotAWorker(string? pipe, string? parent, string named)
    {
        var user = new List<string> { "--melvormods-worker=Mod:host" };
        if (pipe != null)
            user.Add(pipe);
        if (parent != null)
            user.Add(parent);

        var args = WorkerArgs.Parse(Line([.. user]), out var problem);

        Assert.Null(args);
        Assert.NotNull(problem);
        Assert.Contains(named, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--melvormods-locale=fr", "fr")]
    [InlineData("--melvormods-locale=en_GB", "en_GB")]
    [InlineData("--melvormods-locale=zh_Hans_CN", "zh_Hans_CN")]
    public void TheLocaleIsParsed(string flag, string locale)
    {
        var args = WorkerArgs.Parse(
            Line("--melvormods-worker=Mod:host", "--melvormods-pipe=p", "--melvormods-parent=1", flag),
            out var problem
        );

        Assert.Null(problem);
        Assert.Equal(locale, args?.Locale);
    }

    [Fact]
    public void NoLocaleFlagMeansNoLocale()
    {
        var args = WorkerArgs.Parse(
            Line("--melvormods-worker=Mod:host", "--melvormods-pipe=p", "--melvormods-parent=1"),
            out var problem
        );

        Assert.Null(problem);
        Assert.NotNull(args);
        Assert.Null(args.Locale);
    }

    [Theory]
    [InlineData("--melvormods-locale=")]
    [InlineData("--melvormods-locale=fr FR")]
    [InlineData("--melvormods-locale=fr;rm")]
    public void AMalformedLocaleIsAProblem(string flag)
    {
        var args = WorkerArgs.Parse(
            Line("--melvormods-worker=Mod:host", "--melvormods-pipe=p", "--melvormods-parent=1", flag),
            out var problem
        );

        Assert.Null(args);
        Assert.NotNull(problem);
        Assert.Contains("locale", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChildCommandLineCarriesTheLocale()
    {
        var worker = new WorkerArgs("M", "r", "pipe-1", 42, "fr_FR");
        var args = WorkerArgs.Arguments(10, "log", worker);

        Assert.Equal("--melvormods-locale=fr_FR", args[^1]);
        Assert.Equal(worker, WorkerArgs.Parse([@"C:\x\Melvor2.exe", .. args], out var problem));
        Assert.Null(problem);
        Assert.EndsWith(
            " -- --melvormods-worker=M:r --melvormods-pipe=pipe-1 --melvormods-parent=42 --melvormods-locale=fr_FR",
            WorkerArgs.CommandLine(@"C:\x\Melvor2.exe", args),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("fr", true)]
    [InlineData("en_GB", true)]
    [InlineData("sr_Latn", true)]
    [InlineData("", false)]
    [InlineData("fr FR", false)]
    [InlineData("\"fr\"", false)]
    public void OnlyPlainLocaleCodesArePassed(string locale, bool valid)
    {
        Assert.Equal(valid, WorkerArgs.IsLocale(locale));
    }

    [Fact]
    public void LogNamesCarryTheModAndRole()
    {
        var args = new WorkerArgs("CombatSimulator2", "host", "p", 1);

        Assert.Equal("loader.CombatSimulator2.host.log", args.LoaderLog);
        Assert.Equal("mods.CombatSimulator2.host.log", args.ModsLog);
        Assert.Equal("CombatSimulator2.host.godot.log", args.GodotLog);
        Assert.Equal("CombatSimulator2.host.godot.log", WorkerArgs.GodotLogName("CombatSimulator2", "host"));
    }

    [Fact]
    public void TheChildCommandLinePutsGodotFlagsBeforeTheWorkerFlags()
    {
        var args = WorkerArgs.Arguments(10, @"C:\logs\M.r.godot.log", new WorkerArgs("M", "r", "pipe-1", 42));

        Assert.Equal(
            [
                "--headless",
                "--max-fps",
                "10",
                "--log-file",
                @"C:\logs\M.r.godot.log",
                "--",
                "--melvormods-worker=M:r",
                "--melvormods-pipe=pipe-1",
                "--melvormods-parent=42",
            ],
            args
        );
    }

    [Fact]
    public void TheChildCommandLineParsesBackAsTheSameWorker()
    {
        var worker = new WorkerArgs("CombatSimulator2", "host", "MelvorMods.CombatSimulator2.host.9.x", 9);
        var line = WorkerArgs.Arguments(10, "log", worker);

        Assert.Equal(worker, WorkerArgs.Parse([@"C:\x\Melvor2.exe", .. line], out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void TheCommandLineQuotesPathsWithSpaces()
    {
        var line = WorkerArgs.CommandLine(
            @"C:\Program Files\Melvor Idle 2\Melvor2.exe",
            ["--log-file", @"C:\Program Files\Melvor Idle 2\M\logs\M.r.godot.log", "--"]
        );

        Assert.Equal(
            "\"C:\\Program Files\\Melvor Idle 2\\Melvor2.exe\" --log-file "
                + "\"C:\\Program Files\\Melvor Idle 2\\M\\logs\\M.r.godot.log\" --",
            line
        );
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData(@"C:\no\space\", @"C:\no\space\")]
    public void QuotingFollowsTheWindowsRules(string arg, string quoted)
    {
        Assert.Equal(quoted, WorkerArgs.Quote(arg));
    }
}
