using System.Text;
using System.Text.RegularExpressions;

namespace MelvorMods.Core;

/// <summary>
/// The command line that makes a game instance a worker: a headless copy of the game, started by a mod in the live
/// game, that loads only that mod and hands it a pipe back to the live game.
/// </summary>
/// <param name="Locale">The live game's language when the worker started, so game text the worker makes matches it;
/// null when not given (the worker uses the game's start-up language).</param>
sealed partial record WorkerArgs(string Mod, string Role, string Pipe, int ParentPid, string? Locale = null)
{
    private const string WorkerFlag = "--melvormods-worker=";
    private const string PipeFlag = "--melvormods-pipe=";
    private const string ParentFlag = "--melvormods-parent=";
    private const string LocaleFlag = "--melvormods-locale=";

    public string LoaderLog => $"loader.{Mod}.{Role}.log";
    public string ModsLog => $"mods.{Mod}.{Role}.log";
    public string GodotLog => GodotLogName(Mod, Role);

    public static string GodotLogName(string mod, string role) => $"{mod}.{role}.godot.log";

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex Name();

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex PipeName();

    [GeneratedRegex("^[A-Za-z0-9_@-]+$")]
    private static partial Regex LocaleName();

    /// <summary>
    /// Whether <paramref name="locale"/> is a plain Godot locale code (<c>fr</c>, <c>en_GB</c>, <c>sr_Latn</c>).
    /// </summary>
    public static bool IsLocale(string? locale) => locale != null && LocaleName().IsMatch(locale);

    /// <summary>
    /// Reads the worker from a full command line (Godot's own arguments are ignored). No worker flag: null, no problem.
    /// A worker flag with a missing or malformed value, pipe or parent: null, and <paramref name="problem"/> says why.
    /// </summary>
    public static WorkerArgs? Parse(IReadOnlyList<string> args, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(args);
        problem = null;
        var worker = Value(args, WorkerFlag);
        if (worker == null)
            return null;
        var parts = worker.Split(':');
        if (parts.Length != 2 || !Name().IsMatch(parts[0]) || !Name().IsMatch(parts[1]))
        {
            problem = $"worker '{worker}' is not <mod>:<role>";
            return null;
        }
        var pipe = Value(args, PipeFlag);
        if (pipe == null || !PipeName().IsMatch(pipe))
        {
            problem = pipe == null ? "no pipe given" : $"pipe '{pipe}' is not a valid name";
            return null;
        }
        var parent = Value(args, ParentFlag);
        if (
            !int.TryParse(parent, System.Globalization.CultureInfo.InvariantCulture, out var parentPid)
            || parentPid <= 0
        )
        {
            problem = parent == null ? "no parent given" : $"parent '{parent}' is not a process id";
            return null;
        }
        var locale = Value(args, LocaleFlag);
        if (locale != null && !IsLocale(locale))
        {
            problem = $"locale '{locale}' is not a locale code";
            return null;
        }
        return new WorkerArgs(parts[0], parts[1], pipe, parentPid, locale);
    }

    private static string? Value(IReadOnlyList<string> args, string flag) =>
        args.FirstOrDefault(a => a.StartsWith(flag, StringComparison.Ordinal)) is { } arg ? arg[flag.Length..] : null;

    /// <summary>The child's arguments after the exe: Godot's flags, then <c>--</c>, then the worker flags.</summary>
    public static IReadOnlyList<string> Arguments(int maxFps, string logFile, WorkerArgs worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        List<string> args =
        [
            "--headless",
            "--max-fps",
            maxFps.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--log-file",
            logFile,
            "--",
            $"{WorkerFlag}{worker.Mod}:{worker.Role}",
            $"{PipeFlag}{worker.Pipe}",
            $"{ParentFlag}{worker.ParentPid}",
        ];
        if (worker.Locale != null)
            args.Add($"{LocaleFlag}{worker.Locale}");
        return args;
    }

    /// <summary>The exe and arguments as one Windows command line, each quoted as needed.</summary>
    public static string CommandLine(string exe, IEnumerable<string> args) =>
        string.Join(' ', new[] { exe }.Concat(args).Select(Quote));

    /// <summary>One argument quoted so <c>CommandLineToArgvW</c> reads it back unchanged.</summary>
    public static string Quote(string arg)
    {
        ArgumentNullException.ThrowIfNull(arg);
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
            return arg;
        var text = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
}
