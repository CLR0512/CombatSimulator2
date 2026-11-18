using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>
/// CS2's log, set by the mod when it loads (in the live game or the host). Each line carries the writing thread's
/// <see cref="LogScope"/>.
/// </summary>
internal static class Log
{
    private static ModLog? _log;

    internal static void Use(ModLog log) => _log = log;

    public static void Info(string message) => _log?.Info(LogLine.WithContext(message));

    public static void Warn(string message) => _log?.Warn(LogLine.WithContext(message));

    public static void Error(string message) => _log?.Error(LogLine.WithContext(message));

    /// <summary>Writes the exception's type, message and stack after <paramref name="what"/>.</summary>
    public static void Error(string what, Exception error) => Error($"{what}: {error}");

    /// <summary>Writes the exception's type and message after <paramref name="what"/>, without the stack.</summary>
    public static void Warn(string what, Exception error) => Warn($"{what}: {error.GetType().Name}: {error.Message}");

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    /// <summary>
    /// For a failure on a path that runs often: writes it with its stack the first time this place fails this way,
    /// and nothing after.
    /// </summary>
    public static void WarnOnce(string what, Exception error)
    {
        bool first;
        lock (_warned)
            first = _warned.Add($"{what}|{error.GetType().FullName}|{error.Message}");
        if (first)
            Warn($"{what} (said once): {error}");
    }
}

/// <summary>A command error whose message is the reply.</summary>
internal sealed class CommandException(string message) : Exception(message);
