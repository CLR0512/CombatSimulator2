namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// What the host does with a dev message no dev part handled, as in a Release build: it says so once and goes on
/// serving. The message kind's number stays reserved for dev builds.
/// </summary>
internal sealed class DevFrameFallback
{
    private bool _logged;

    public void NotHandled(Action<string> warn)
    {
        if (_logged)
            return;
        _logged = true;
        warn("dev message ignored");
    }
}
