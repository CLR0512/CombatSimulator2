using GamesByMalcs.Melvor2.Components.Pets;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// Pets: the pets whose bonuses reach a fight or its results, which a side can add or take away, then the others
/// (those that help only their own skill, or give nothing), shown only under the All filter. Whether a pet reaches a
/// fight is read from its bonuses, never from its name.
/// </summary>
internal static class PetsPage
{
    /// <summary>Every pet the skills keep, with its state, in the game's order.</summary>
    public static List<ActivePet> Active(MelvorGame game) => [.. All(game).Select(p => p.Active).OfType<ActivePet>()];

    /// <summary>
    /// Every pet of the build in the game's order, with its state where a skill keeps it; one no skill keeps can't be
    /// found in this build.
    /// </summary>
    public static List<(Pet Pet, ActivePet? Active)> All(MelvorGame game)
    {
        var byPet = game
            .Entities.GetAllObjects()
            .SelectMany(entity => entity.Components.Values.OfType<PetsComponent>())
            .SelectMany(pets => pets.ActivePets.Values)
            .DistinctBy(active => active.Pet)
            .ToDictionary(active => active.Pet);
        return [.. game.Data.GetDataRegistryOutput<Pet>().GetAllObjects().Select(p => (p, byPet.GetValueOrDefault(p)))];
    }

    /// <summary>Whether the save has each pet's bonuses on: found, by the save.</summary>
    public static void Values(MelvorGame game, Dictionary<ChangeKey, int> values)
    {
        if (!Dependencies.IsAvailable(Dependencies.Pets))
            return;
        foreach (var active in Active(game))
            values[new ChangeKey(ChangeKind.Pet, active.Pet.Id)] = PetChange.Has(game, active.Pet) ? 1 : 0;
    }

    public static SetupPage Read(MelvorGame game, IReadOnlyDictionary<ChangeKey, int> save)
    {
        if (!Dependencies.IsAvailable(Dependencies.Pets))
            return PageReader.Unsupported(SetupPage.Pets);
        var pets = All(game);
        if (pets.Count == 0)
            return PageReader.Page(SetupPage.Pets, PageState.NoData, Note.Of("Not in this build"));
        var combat = new List<PageEntry>();
        var other = new List<PageEntry>();
        foreach (var (pet, active) in pets)
        {
            var found = active?.IsUnlocked == true;
            var key = new ChangeKey(ChangeKind.Pet, pet.Id);
            var name = ((GamesByMalcs.Melvor2.Data.INamedObject)pet).DisplayName.Text;
            var bonuses = pet.Bonuses;
            var gives = bonuses == null ? [] : SetupSheets.Describe(ModifierGroup.Pet, pet.Id, name, bonuses);
            if (active != null && bonuses != null && CombatReach.Of(bonuses))
            {
                var has = PetChange.Has(game, pet) ? 1 : 0;
                combat.Add(
                    new PageEntry(
                        pet.Id,
                        name,
                        pet.Id,
                        EntryKind.Toggle,
                        has,
                        1,
                        PageReader.Saved(save, key, has),
                        [],
                        gives,
                        key,
                        null,
                        Description: Described(pet.Description)
                    )
                );
                continue;
            }
            other.Add(
                new PageEntry(
                    pet.Id,
                    name,
                    pet.Id,
                    EntryKind.Info,
                    found ? 1 : 0,
                    1,
                    found ? 1 : 0,
                    [],
                    gives,
                    null,
                    null,
                    Notable: false,
                    Description: Described(pet.Description),
                    Remark: gives.Count == 0 ? Note.Of("No bonus") : null
                )
            );
        }
        var groups = new List<PageGroup>();
        if (combat.Count > 0)
            groups.Add(
                new PageGroup(
                    "combat",
                    "",
                    null,
                    Note.Of("{0} of {1} found", combat.Count(e => e.SaveValue > 0), combat.Count),
                    combat,
                    Note.Of("In combat")
                )
            );
        if (other.Count > 0)
            groups.Add(
                new PageGroup(
                    "other",
                    "",
                    null,
                    Note.Of("Help only their own skill, or give nothing"),
                    other,
                    Note.Of("Other pets ({0})", other.Count)
                )
            );
        return new SetupPage(
            SetupPage.Pets,
            PageSection.Account,
            PageState.Changeable,
            Note.Of("Found pets always give their bonus."),
            [],
            groups
        );
    }

    private static string? Described(string? text)
    {
        var plain = text == null ? "" : Targets.Plain(text);
        return plain.Length > 0 ? plain : null;
    }
}
