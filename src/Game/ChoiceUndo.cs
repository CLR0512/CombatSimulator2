using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The prayers, spell, imbuement, food, combat toolbelt, potion, pets and technique levels of a copy at one moment,
/// so choices changed since can be put back through the game's own calls. As with gear, a reset alone reads them back
/// from their saved bytes, which raises none of the events the character's modifiers and conditions follow; a pet's
/// bonuses aren't saved at all.
/// </summary>
internal sealed class ChoiceUndo
{
    private readonly Copy _copy;
    private readonly ChangeList _back;

    private ChoiceUndo(Copy copy, ChangeList back)
    {
        _copy = copy;
        _back = back;
    }

    /// <summary>The choices of <paramref name="copy"/> as they are now.</summary>
    public static ChoiceUndo Of(Copy copy) => new(copy, Held(copy));

    /// <summary>
    /// Puts back what differs, prayers turned off before others go on. Returns the changes the game refused.
    /// </summary>
    public IReadOnlyList<ChangeOutcome> Restore()
    {
        var now = Held(_copy);
        var back = new List<Change>();
        foreach (var change in _back)
        {
            if (now.Find(change.Key) is { } held && held.Value == change.Value)
                continue;
            back.Add(change);
        }
        // A prayer turned on since was off before: it goes off.
        foreach (var change in now.Where(c => c.Kind == ChangeKind.Prayer && c.TurnsOn))
            if (_back.Find(change.Key) == null)
                back.Add(new Change(ChangeKind.Prayer, change.Subject, Change.Off));
        if (back.Count == 0)
            return [];
        return
        [
            .. Changes
                .Apply(_copy, ChangeList.Of(back), ignoreRequirements: true, throwOnRefusal: false)
                .Where(outcome => outcome.LeftOut),
        ];
    }

    // What the copy holds, as changes that would set it: the prayers on, the spell, the imbuement, the food, each
    // combat toolbelt slot but the potion's, the potion, whether each pet's bonuses are on, and each technique's level.
    private static ChangeList Held(Copy copy)
    {
        using var scope = Guards.Enter();
        var combat = copy.Combat;
        var held = new List<Change>();
        foreach (var prayer in combat.Prayer.ActivePrayers.Keys.Where(combat.Prayer.IsPrayerSelected))
            held.Add(new Change(ChangeKind.Prayer, prayer.Id, Change.On));
        if (combat.MagicSpells.SelectedSpell?.Data.Id is { } spell)
            held.Add(new Change(ChangeKind.Spell, null, spell));
        held.Add(new Change(ChangeKind.Imbuement, null, combat.MagicSpells.SelectedImbuement?.Data.Id ?? Change.None));
        held.Add(new Change(ChangeKind.Food, null, combat.Food.SelectedFood?.Item.Id ?? Change.None));
        foreach (
            var slot in combat.Toolbelt._activeSlots.Values.Where(s =>
                s.Slot.FitsType.Id != Changes.CombatPotionSlot && s.Slot.UnlockRequirements.AreMet
            )
        )
            held.Add(new Change(ChangeKind.Toolbelt, slot.Slot.Id, slot.Equipped?.Item.Id ?? Change.None));
        if (
            Dependencies.IsAvailable(Dependencies.Herblore)
            && PotionChange.Slot(combat) is { UnlockRequirements.AreMet: true } potion
            && combat.Toolbelt.TryGetActiveSlot(potion, out var active)
        )
            held.Add(new Change(ChangeKind.Potion, null, active.Equipped?.Item.Id ?? Change.None));
        if (Dependencies.IsAvailable(Dependencies.Pets))
            foreach (var pet in Pages.PetsPage.Active(copy.Game))
                held.Add(
                    new Change(ChangeKind.Pet, pet.Pet.Id, PetChange.Has(copy.Game, pet.Pet) ? Change.On : Change.Off)
                );
        if (Dependencies.IsAvailable(Dependencies.Agility))
            foreach (var technique in TechniqueChange.Techniques(copy.Game).Where(t => t.Experience.Level >= 1))
                held.Add(
                    new Change(
                        ChangeKind.Technique,
                        technique.Technique.Id,
                        technique.Experience.Level.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    )
                );
        return ChangeList.Of(held);
    }
}
