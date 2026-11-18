using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ChangeNotesTests
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["MelvorBase:Strength"] = "Strength",
        ["MelvorBase:Defence"] = "Defence",
        ["MelvorBase:Weapon"] = "Weapon",
        ["MelvorBase:Iron_Sword"] = "Iron Sword",
        ["MelvorBase:Steel_Sword"] = "Steel Sword",
        ["MelvorBase:Rune_Sword"] = "Rune Sword",
        ["MelvorBase:Shrimp"] = "Shrimp",
        ["MelvorBase:Thick_Skin"] = "Thick Skin",
        ["MelvorBase:Slash"] = "Slash",
        ["MelvorBase:Stab"] = "Stab",
        ["MelvorBase:AirStrike"] = "Air Strike",
        ["MelvorBase:AirBolt"] = "Air Bolt",
        ["MelvorBase:SuffocateI"] = "Suffocate I",
        ["MelvorBase:Bruno"] = "Bruno",
        ["MelvorBase:Oak"] = "Oak",
        ["MelvorBase:Climbing"] = "Climbing",
        ["MelvorBase:MeleePotionP1A"] = "Melee Potion I",
    };

    private static string Name(string id) => Names.GetValueOrDefault(id, id);

    private static ChangeOutcome Outcome(
        string change,
        string? from,
        ChangeState state = ChangeState.Accepted,
        params Note[] reasons
    ) => new(Change.Parse(change), from, state, [.. reasons]);

    private static List<string> English(params ChangeOutcome[] outcomes) =>
        [.. ChangeNotes.Of(outcomes, Name).Select(n => n.English())];

    [Fact]
    public void EachAcceptedChangeIsOneNote() =>
        Assert.Equal(
            ["Strength 50 → 60", "Weapon: Iron Sword → Steel Sword", "Food: Shrimp → none", "Thick Skin on"],
            English(
                Outcome("level=Strength:60", "50"),
                Outcome("equip=Weapon:Steel_Sword", "MelvorBase:Iron_Sword"),
                Outcome("food=none", "MelvorBase:Shrimp"),
                Outcome("prayer=Thick_Skin:on", "off")
            )
        );

    [Fact]
    public void PageChangesAreNamed() =>
        Assert.Equal(
            [
                "Bruno found",
                "Bruno removed",
                "Oak bonfire, burning",
                "Bonfire out",
                "Climbing 12 → 40",
                "Potion: none → Melee Potion I",
                Assumption.PetsChanged.Note.English(),
            ],
            English(
                Outcome("pet=Bruno:on", "off"),
                Outcome("pet=Bruno:off", "on"),
                Outcome("bonfire=Oak", "out"),
                Outcome("bonfire=out", "MelvorBase:Oak"),
                Outcome("technique=Climbing:40", "12"),
                Outcome("potion=MeleePotionP1A", "none")
            )
        );

    [Fact]
    public void EmptySlotsAndNoFoodAreNamed() =>
        Assert.Equal(
            ["Weapon: Iron Sword → empty", "Weapon: empty → Iron Sword", "Food: none → Shrimp", "Thick Skin off"],
            English(
                Outcome("equip=Weapon:none", "MelvorBase:Iron_Sword"),
                Outcome("equip=Weapon:Iron_Sword", "none"),
                Outcome("food=Shrimp", "none"),
                Outcome("prayer=Thick_Skin:off", "on")
            )
        );

    [Fact]
    public void RefusedAndIgnoredChangesSayWhy() =>
        Assert.Equal(
            [
                "left out: Weapon: Iron Sword → Rune Sword (Requires Attack level 40)",
                "Weapon: Iron Sword → Steel Sword: requirement ignored (Requires Attack level 5; Requires Defence level 5)",
            ],
            English(
                Outcome(
                    "equip=Weapon:Rune_Sword",
                    "MelvorBase:Iron_Sword",
                    ChangeState.Refused,
                    Note.Text("Requires Attack level 40")
                ),
                Outcome(
                    "equip=Weapon:Steel_Sword",
                    "MelvorBase:Iron_Sword",
                    ChangeState.RequirementIgnored,
                    Note.Text("Requires Attack level 5"),
                    Note.Text("Requires Defence level 5")
                )
            )
        );

    [Fact]
    public void AStanceIsNamed() =>
        Assert.Equal(["Stance: Slash → Stab"], English(Outcome("stance=Stab", "MelvorBase:Slash")));

    [Fact]
    public void SpellsAndImbuementsAreNamed() =>
        Assert.Equal(
            [
                "Spell: Air Strike → Air Bolt",
                "Spell: none → Air Bolt",
                "Imbuement: none → Suffocate I",
                "Imbuement: Suffocate I → none",
            ],
            English(
                Outcome("spell=AirBolt", "MelvorBase:AirStrike"),
                Outcome("spell=AirBolt", "none"),
                Outcome("imbuement=SuffocateI", "none"),
                Outcome("imbuement=none", "MelvorBase:SuffocateI")
            )
        );

    [Fact]
    public void NoChangeHasNoNote() =>
        Assert.Empty(English(Outcome("food=Shrimp", "MelvorBase:Shrimp", ChangeState.NoChange)));

    [Fact]
    public void LoweringALevelAddsItsAssumption()
    {
        var lowered = English(Outcome("level=Defence:1", "40"));
        Assert.Equal(["Defence 40 → 1", Assumption.LevelsLowered.Note.English()], lowered);
        Assert.DoesNotContain(Assumption.LevelsLowered.Note.English(), English(Outcome("level=Defence:50", "40")));
        // A refused lowering lowers nothing.
        Assert.DoesNotContain(
            Assumption.LevelsLowered.Note.English(),
            English(Outcome("level=Defence:1", "40", ChangeState.Refused, Note.Of("bank full")))
        );
    }

    [Fact]
    public void APetChangeSaysTheFoundCountIsTheSaves()
    {
        Assert.Equal(["Bruno found", Assumption.PetsChanged.Note.English()], English(Outcome("pet=Bruno:on", "off")));
        Assert.DoesNotContain(
            Assumption.PetsChanged.Note.English(),
            English(Outcome("pet=Bruno:on", "off", ChangeState.Refused, Note.Of("not found yet")))
        );
        Assert.False(Assumption.PetsChanged.InEveryFight);
        Assert.DoesNotContain(Assumption.All, a => a.Key == Assumption.PetsChanged.Key);
    }

    [Fact]
    public void TheLevelsLoweredAssumptionIsNotInEveryResult()
    {
        Assert.False(Assumption.LevelsLowered.InEveryFight);
        Assert.DoesNotContain(Assumption.All, a => a.Key == Assumption.LevelsLowered.Key);
    }

    [Fact]
    public void TheLabelCountsTheChanges()
    {
        Assert.Equal("current + 1 change", ChangeNotes.Label("current", 1).English());
        Assert.Equal("current + 2 changes", ChangeNotes.Label("current", 2).English());
    }

    [Fact]
    public void ANoteCanHoldANote()
    {
        var note = Note.Of("left out: {0} ({1})", Note.Of("{0} on", "Thick Skin"), Note.Text("Prayer 5"));
        Assert.Equal("left out: Thick Skin on (Prayer 5)", note.English());
        var read = JsonSerializer.Deserialize<Note>(JsonSerializer.Serialize(note, RunFile.Options), RunFile.Options)!;
        Assert.Equal(note.English(), read.English());
        Assert.IsType<Note>(read.Args[0]);
    }

    [Fact]
    public void ResultsListTheChangesWithTheirAssumptions()
    {
        var run = ResultsModelTests.SampleRun();
        run.ChangeOutcomes = [Outcome("level=Strength:60", "50")];
        var result = run.Results[run.Targets[0]];
        var listed = ChangeNotes.Assumptions(run, result, Name).Select(n => n.English()).ToList();
        Assert.Equal([.. result.Assumptions.Select(n => n.English()), "Strength 50 → 60"], listed);
        run.ChangeOutcomes = null;
        Assert.Equal(result.Assumptions, ChangeNotes.Assumptions(run, result, Name));
    }

    [Theory]
    [InlineData("none", "Kuren loadouts: none → Golbin: Loadout1")]
    [InlineData("off", "Kuren loadouts: off → Golbin: Loadout1")]
    [InlineData(null, "Kuren loadouts: ? → Golbin: Loadout1")]
    public void ALoadoutsChangeReadsFromWhateverItWas(string? from, string expected) =>
        Assert.Equal(
            expected,
            ChangeNotes.Describe(Change.Parse("loadouts=Kuren:Golbin@Loadout1"), from, Ids.Brief).English()
        );

    [Fact]
    public void AWholePoolOnLoadoutsCountsThemByLoadout() =>
        Assert.Equal(
            "Kuren loadouts: none → Loadout1 ×2, Loadout3 ×1",
            ChangeNotes
                .Describe(
                    Change.Parse("loadouts=Kuren:Golbin@Loadout1,Scarecrow@Loadout1,JackOLantern@Loadout3"),
                    "none",
                    Ids.Brief
                )
                .English()
        );
}
