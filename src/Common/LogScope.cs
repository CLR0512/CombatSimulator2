using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Common;

/// <summary>
/// The work this thread is doing, which CS2's log writes before each message: the run, the comparison's side, the
/// phase, the target and the copy. Whoever owns a piece of work pushes its part, per run, phase, target and copy,
/// never per tick, and disposing restores the outer scope. A thread CS2 starts begins in the scope of the thread
/// that started it (see <see cref="Enter"/>).
/// </summary>
internal readonly record struct LogScope(
    string? Run = null,
    char? Side = null,
    RunPhase? Phase = null,
    string? Target = null,
    int? Copy = null
)
{
    [ThreadStatic]
    private static LogScope _current;

    public static LogScope Current => _current;

    /// <summary>Sets the fields given over the current scope, keeping the others.</summary>
    public static Restore Push(
        string? run = null,
        char? side = null,
        RunPhase? phase = null,
        string? target = null,
        int? copy = null
    )
    {
        var outer = _current;
        _current = new LogScope(
            run ?? outer.Run,
            side ?? outer.Side,
            phase ?? outer.Phase,
            target ?? outer.Target,
            copy ?? outer.Copy
        );
        return new Restore(outer);
    }

    /// <summary>Replaces the current scope, for a new thread taking its starter's scope.</summary>
    public static Restore Enter(LogScope scope)
    {
        var outer = _current;
        _current = scope;
        return new Restore(outer);
    }

    public readonly struct Restore(LogScope outer) : IDisposable
    {
        public void Dispose() => _current = outer;
    }
}

/// <summary>Renders a scope as the line's context block.</summary>
internal static class LogLine
{
    /// <summary><c>run=… side=… phase=… target=… copy=… | </c>, set keys only; empty when none is set.</summary>
    public static string Context(LogScope scope)
    {
        if (scope == default)
            return "";
        var text = new StringBuilder();
        void Add(string key, string value)
        {
            if (text.Length > 0)
                text.Append(' ');
            text.Append(key).Append('=').Append(value);
        }
        if (scope.Run != null)
            Add("run", scope.Run);
        if (scope.Side is { } side)
            Add("side", side.ToString());
        if (scope.Phase is { } phase)
            Add("phase", FailureRecord.Name(phase));
        if (scope.Target != null)
            Add("target", scope.Target);
        if (scope.Copy is { } copy)
            Add("copy", copy.ToString(CultureInfo.InvariantCulture));
        return text.Append(" | ").ToString();
    }

    /// <summary>The message with the current thread's context before it.</summary>
    public static string WithContext(string message) => Context(LogScope.Current) + message;
}
