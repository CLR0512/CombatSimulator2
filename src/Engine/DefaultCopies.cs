namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>How many copies fight at once when the player hasn't chosen: a rule of the machine's cores.</summary>
internal static partial class DefaultCopies
{
    /// <summary>The most copies the default ever asks for.</summary>
    public const int Most = 4;

    /// <summary>
    /// <c>min(cores − 2, 4)</c>, at least 1: two cores stay with the game and the system, and past four copies a
    /// run gains too little for the memory each one takes.
    /// </summary>
    public static int For(int cores) => Math.Max(1, Math.Min(cores - 2, Most));

    /// <summary>The logical cores the rule reads on this machine.</summary>
    public static int Cores
    {
        get
        {
            var cores = Environment.ProcessorCount;
            OverrideCores(ref cores);
            return cores;
        }
    }

    /// <summary>The default on this machine.</summary>
    public static int Now => For(Cores);

    /// <summary>The bare copies the host keeps ready while the panel is open: one per default fight slot.</summary>
    public static int Stock
    {
        get
        {
            var stock = Now;
            OverrideStock(ref stock);
            return stock;
        }
    }

    // Dev builds only: another core count, to see what a smaller machine gets.
    static partial void OverrideCores(ref int cores);

    // Dev builds only: another size for the warm stock, to measure a smaller one.
    static partial void OverrideStock(ref int stock);
}
