using GamesByMalcs.Melvor2.Entities.Firemaking;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The bonfire: raised tier by tier through the game's upgrade, each tier's cost given and its requirements checked
/// (the upgrade itself checks none, as the game's button does), then lit with its burn timer filled; or put out with
/// the timer emptied. The game's own handler adds or removes the bonus as the timer gains or loses its time. Nothing
/// lowers a bonfire. Copies only.
/// </summary>
internal static class BonfireChange
{
    public const string FiremakingId = "MelvorBase:Firemaking";

    public static FiremakingBonfireComponent? Component(MelvorGame game) =>
        game.Entities.TryGetObjectById(FiremakingId, out var entity) && entity is FiremakingEntity firemaking
            ? firemaking.Bonfire
            : null;

    /// <summary>Whether the bonfire gives its bonus: a tier, time left, and lit, as the game reads it at load.</summary>
    public static bool Burning(FiremakingBonfireComponent bonfire) =>
        bonfire.CurrentBonfire != null && bonfire.DurationTimer.HasTime && bonfire.IsActive;

    /// <summary>The bonfire as a change's value: its tier while it burns, else out.</summary>
    public static string Held(FiremakingBonfireComponent bonfire) =>
        Burning(bonfire) ? bonfire.CurrentBonfire!.Id : Change.Out;

    /// <summary>The build's tiers in the game's order.</summary>
    public static List<FiremakingBonfire> Tiers(FiremakingBonfireComponent bonfire)
    {
        var tiers = new List<FiremakingBonfire>();
        for (var i = 0; i < bonfire.Bonfires.Count; i++)
            tiers.Add(bonfire.Bonfires[i]);
        return tiers;
    }

    public static ChangeOutcome Apply(Copy copy, Change change, bool ignoreRequirements)
    {
        if (Component(copy.Game) is not { } bonfire)
            return Changes.Refused(change, null, Note.Of("no bonfire"));
        var from = Held(bonfire);
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        if (change.Tier is not { } tierId)
        {
            Out(bonfire);
            return Burning(bonfire)
                ? Changes.Refused(change, from, Note.Of("the game kept it burning"))
                : new(change, from, ChangeState.Accepted, []);
        }
        var tiers = Tiers(bonfire);
        var to = tiers.FindIndex(t => t.Id == tierId);
        if (to < 0)
            return Changes.Missing(change, from);
        var now = bonfire.CurrentBonfire is { } current ? tiers.IndexOf(current) : -1;
        if (to < now)
            return Changes.Refused(change, from, Note.Of("the game can't lower a bonfire"));
        var steps = tiers.GetRange(now + 1, to - now);
        var reasons = steps.SelectMany(step => Targets.Unmet(step.Requirements)).ToList();
        if (reasons.Count > 0 && !ignoreRequirements)
            return Changes.Refused(change, from, [.. reasons]);
        foreach (var step in steps)
        {
            if (!Supplies.Ingredients(copy, step.Costs, 1))
                return Changes.Refused(change, from, Note.Of("bank full"));
            bonfire.PurchaseNextBonfire();
            if (bonfire.CurrentBonfire != step)
                return Changes.Refused(change, from, Note.Of("the game didn't raise it"));
        }
        Light(bonfire);
        var applied = bonfire.CurrentBonfire == tiers[to] && Burning(bonfire);
        if (!applied && reasons.Count == 0)
            reasons.Add(Note.Of("the game didn't light it"));
        return Changes.Outcome(change, from, applied, reasons, ignoreRequirements);
    }

    /// <summary>A copy's bonfire as it was: its tier and whether it burned.</summary>
    public sealed record Kept(FiremakingBonfire? Tier, bool Burning);

    public static Kept? Keep(Copy copy) =>
        Component(copy.Game) is { } bonfire ? new Kept(bonfire.CurrentBonfire, Burning(bonfire)) : null;

    /// <summary>
    /// Puts the bonus back as <paramref name="kept"/> had it, before a reset reads the bonfire back from its saved
    /// bytes: that raises none of the events the bonus follows, and the game has no call that lowers a tier.
    /// </summary>
    public static void Restore(Copy copy, Kept kept)
    {
        if (Component(copy.Game) is not { } bonfire)
            return;
        var burning = Burning(bonfire);
        if (bonfire.CurrentBonfire == kept.Tier && burning == kept.Burning)
            return;
        if (burning)
            copy.Game.Modifiers.Remove(bonfire.CurrentBonfire!, bonfire.CurrentBonfire!.Bonuses);
        if (kept is { Burning: true, Tier: { } tier })
            copy.Game.Modifiers.AddOrUpdate(tier, tier.Bonuses);
    }

    // The timer emptied and filled, which the game's handler answers with the tier's bonus. A save with time left
    // but not lit loads without the bonus, and filling a timer that has time raises nothing: it starts from empty.
    private static void Light(FiremakingBonfireComponent bonfire)
    {
        bonfire.DurationTimer.TicksLeft = 0;
        bonfire.IsActive = true;
        bonfire.DurationTimer.TicksLeft = bonfire.DurationTimer.MaxTicks;
    }

    // The timer emptied, which the game's handler answers by taking the bonus away.
    private static void Out(FiremakingBonfireComponent bonfire)
    {
        bonfire.DurationTimer.TicksLeft = 0;
        bonfire.IsActive = false;
    }
}
