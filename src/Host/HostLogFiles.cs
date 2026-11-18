namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// A new host empties its logs when it starts, so the game moves the last host's logs aside first: to
/// <c>.crash.log</c> after a crash or hang, where later ordinary restarts can't overwrite them, otherwise to
/// <c>.previous.log</c>. Each name keeps one copy, replaced by the next.
/// </summary>
internal static class HostLogFiles
{
    public const string HostLog = "CombatSimulator2.host.log";
    public const string GodotLog = "CombatSimulator2.host.godot.log";

    public static string KeptName(string name, bool crashed) =>
        Path.GetFileNameWithoutExtension(name) + (crashed ? ".crash.log" : ".previous.log");

    /// <summary>
    /// Copies a lost host's logs to their crash names at once, so a report saved before the next host starts holds
    /// this host's logs rather than an earlier crash's; the next start moves them there again.
    /// </summary>
    public static void CopyAsCrash(string folder, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(warn);
        foreach (var name in (string[])[HostLog, GodotLog])
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path))
                continue;
            try
            {
                File.Copy(path, Path.Combine(folder, KeptName(name, crashed: true)), overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                warn($"{name} not kept: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Moves each log there is in <paramref name="folder"/>; a log that can't be moved is only reported. A host just
    /// stopped holds its Godot log until it has exited, so a busy file is tried again for up to
    /// <paramref name="patience"/> (2 s by default): call it off the main thread.
    /// </summary>
    public static void Keep(string folder, bool crashed, Action<string> warn, TimeSpan? patience = null)
    {
        ArgumentNullException.ThrowIfNull(warn);
        var until = DateTime.UtcNow + (patience ?? TimeSpan.FromSeconds(2));
        foreach (var name in (string[])[HostLog, GodotLog])
        {
            var path = Path.Combine(folder, name);
            while (File.Exists(path))
            {
                try
                {
                    File.Move(path, Path.Combine(folder, KeptName(name, crashed)), overwrite: true);
                    break;
                }
                catch (IOException) when (DateTime.UtcNow < until)
                {
                    Thread.Sleep(100);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    warn($"{name} not kept: {e.Message}");
                    break;
                }
            }
        }
    }
}
