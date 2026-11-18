namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>What the help layer over CS2's window shows: nothing, the About window, the Guide window, the Gear tab's guide, or the tour.</summary>
internal enum HelpShown
{
    None,
    About,
    Explainer,
    GearGuide,
    Tour,
}

/// <summary>
/// The help layer's state, kept while CS2's window is rebuilt (a skin or language change) and cleared when it closes.
/// Nothing here is saved.
/// </summary>
internal sealed class HelpState
{
    public HelpShown Shown { get; set; }

    /// <summary>The tour's step, 1 to the last; 0 without a tour.</summary>
    public int Step { get; set; }

    /// <summary>The view shown when the tour started, put back when it ends.</summary>
    public string? ViewBefore { get; set; }
}
