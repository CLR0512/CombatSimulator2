namespace MelvorMods.CombatSimulator2.Game;

internal sealed partial class CopyConditions
{
    /// <summary>
    /// For checks: how many conditions were collected, how many listen and how many of those are met.
    /// </summary>
    public string Summary()
    {
        var listening = _all.Where(c => Access.For(c.GetType())?.IsListening(c) == true).ToList();
        return $"{_all.Count} conditions, {listening.Count} listening, {listening.Count(c => Access.For(c.GetType())!.CachedMet(c))} met";
    }
}
