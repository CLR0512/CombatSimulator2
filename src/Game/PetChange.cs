using GamesByMalcs.Melvor2.Components.Pets;
using MelvorMods.CombatSimulator2.Game.Pages;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// A pet added or taken away: its bonuses put into or taken out of the copy's modifiers with the pet as their source,
/// the calls the pets component makes when a pet is found or lost. No call marks a pet found, so the game's found
/// count stays the save's. A pet not found is refused while requirements are checked. Copies only.
/// </summary>
internal static class PetChange
{
    public static ChangeOutcome Apply(Copy copy, Change change, bool ignoreRequirements)
    {
        if (PetsPage.Active(copy.Game).FirstOrDefault(a => a.Pet.Id == change.Subject) is not { } active)
            return Changes.Missing(change, null);
        var pet = active.Pet;
        var from = Has(copy.Game, pet) ? Change.On : Change.Off;
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        var bonuses = pet.Bonuses ?? GamesByMalcs.Melvor2.Modifiers.ModifierValues.Placeholder;
        if (change.Value == Change.Off)
        {
            copy.Game.Modifiers.Remove(pet, bonuses);
            return Has(copy.Game, pet)
                ? Changes.Refused(change, from, Note.Of("the game kept it"))
                : new(change, from, ChangeState.Accepted, []);
        }
        List<Note> reasons = active.IsUnlocked ? [] : [Note.Of("not found yet")];
        if (reasons.Count > 0 && !ignoreRequirements)
            return Changes.Refused(change, from, [.. reasons]);
        copy.Game.Modifiers.AddOrUpdate(pet, bonuses);
        return Changes.Outcome(change, from, Has(copy.Game, pet), reasons, ignoreRequirements);
    }

    /// <summary>Whether the pet's bonuses are among the game's modifiers: found and not taken away, or added.</summary>
    public static bool Has(MelvorGame game, Pet pet) =>
        game.Modifiers.Values.TryGetValue(pet, out var values)
        && values.ContainsKey(pet.Bonuses ?? GamesByMalcs.Melvor2.Modifiers.ModifierValues.Placeholder);
}
