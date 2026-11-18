using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Host;

/// <summary>The process CS2 runs in, as the host link sees it from either end.</summary>
internal static class ThisProcess
{
    /// <summary>This process's game build and assemblies, as the hello reports them.</summary>
    public static HostIdentity Identity() =>
        new(
            LiveGame.GameBuild,
            LiveGame.AssemblyMvid,
            typeof(ThisProcess).Assembly.ManifestModule.ModuleVersionId.ToString()
        );
}
