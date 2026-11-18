using MelvorMods.CombatSimulator2.Common;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>Replies to a command once work that answers through a callback is done, on the main thread.</summary>
internal static class MainThreadReply
{
    /// <summary>
    /// Starts work that calls back with a value or an error, and replies with <paramref name="reply"/> on the main
    /// thread once it has; when <paramref name="timeout"/> passes first, the call fails with
    /// <paramref name="timedOut"/>.
    /// </summary>
    public static void ReplyWhen<T>(
        CommandCall call,
        Action<Action<T?, Exception?>> start,
        Action<T?, Exception?> reply,
        TimeSpan timeout,
        string timedOut
    )
        where T : class
    {
        T? value = null;
        Exception? error = null;
        var done = false;
        start(
            (v, e) =>
            {
                value = v;
                error = e;
                done = true;
            }
        );
        MainThread.When(() => done, () => reply(value, error), timeout, () => call.Error(timedOut));
    }
}
