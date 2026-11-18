namespace MelvorMods.CombatSimulator2.Host.Dev;

/// <summary>
/// Variables added to, or with an empty value taken out of, the environment of hosts started from now: how a
/// runtime setting is tried on the host without a build.
/// </summary>
internal static class EnvironmentOverrides
{
    public static Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    public static void Apply(Dictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        foreach (var (name, value) in Variables)
        {
            if (value.Length == 0)
                environment.Remove(name);
            else
                environment[name] = value;
        }
    }
}
