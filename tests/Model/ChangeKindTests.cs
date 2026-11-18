using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ChangeKindTests
{
    // Saved runs and identities hold changes in this order; a new kind may only be placed between, never reorder.
    [Fact]
    public void KindsAreInCanonicalOrder() =>
        Assert.Equal(
            [
                "Level",
                "Quest",
                "Shop",
                "Guild",
                "Star",
                "Equip",
                "Toolbelt",
                "Stance",
                "Spell",
                "Imbuement",
                "Food",
                "Potion",
                "Prayer",
                "Blocks",
                "Loadouts",
                "SlayerTask",
                "SlayerTaskFor",
                "Pet",
                "Bonfire",
                "Technique",
            ],
            Enum.GetNames<ChangeKind>()
        );

    [Theory]
    [InlineData("quest=MQ3:done", ChangeKind.Quest, "MelvorBase:MQ3", "done")]
    [InlineData("quest=MQ3:open", ChangeKind.Quest, "MelvorBase:MQ3", "open")]
    [InlineData("shop=AutoEat2:1", ChangeKind.Shop, "MelvorBase:AutoEat2", "1")]
    [InlineData("shop=AutoEat2:999", ChangeKind.Shop, "MelvorBase:AutoEat2", "999")]
    [InlineData("guild=Attack:2", ChangeKind.Guild, "MelvorBase:Attack", "2")]
    [InlineData("guild=Attack:0", ChangeKind.Guild, "MelvorBase:Attack", "0")]
    [InlineData("star=Terra_Minor_3:on", ChangeKind.Star, "MelvorBase:Terra_Minor_3", "on")]
    [InlineData("toolbelt=CombatUtility1:none", ChangeKind.Toolbelt, "MelvorBase:CombatUtility1", "none")]
    [InlineData(
        "toolbelt=CombatUtility1:Whetstone",
        ChangeKind.Toolbelt,
        "MelvorBase:CombatUtility1",
        "MelvorBase:Whetstone"
    )]
    [InlineData("pet=Bruno:on", ChangeKind.Pet, "MelvorBase:Bruno", "on")]
    [InlineData("bonfire=Oak", ChangeKind.Bonfire, null, "MelvorBase:Oak")]
    [InlineData("bonfire=out", ChangeKind.Bonfire, null, "out")]
    [InlineData("technique=Climbing:40", ChangeKind.Technique, "MelvorBase:Climbing", "40")]
    public void NewFormsRoundTrip(string text, ChangeKind kind, string? subject, string value)
    {
        var change = Change.Parse(text);
        Assert.Equal(kind, change.Kind);
        Assert.Equal(subject, change.Subject);
        Assert.Equal(value, change.Value);
        Assert.Equal(text, change.ShortForm);
        Assert.Equal(change, Change.Parse(change.ShortForm));
    }

    [Theory]
    [InlineData("toolbelt=CombatUtility1:Other:Stone", "toolbelt=MelvorBase:CombatUtility1:Other:Stone")]
    [InlineData("toolbelt=Other:Slot:Other:Stone", "toolbelt=Other:Slot:Other:Stone")]
    [InlineData("quest=Other:Q1:done", "quest=Other:Q1:done")]
    [InlineData("shop=MelvorBase:AutoEat2:01", "shop=AutoEat2:1")]
    [InlineData("guild=MelvorBase:Attack:2", "guild=Attack:2")]
    [InlineData("star=Other:Node:on", "star=Other:Node:on")]
    public void NamespacesAndNumbersHaveOneShortForm(string text, string expected)
    {
        var change = Change.Parse(text);
        Assert.Equal(expected, change.ShortForm);
        Assert.Equal(change, Change.Parse(change.ShortForm));
    }

    [Theory]
    [InlineData("quest=MQ3")]
    [InlineData("quest=MQ3:started")]
    [InlineData("shop=AutoEat2:0")]
    [InlineData("shop=AutoEat2:1000")]
    [InlineData("shop=AutoEat2:two")]
    [InlineData("guild=Attack:10")]
    [InlineData("guild=Attack:-1")]
    [InlineData("star=Terra_Minor_3:off")]
    [InlineData("star=Terra_Minor_3")]
    [InlineData("toolbelt=CombatUtility1")]
    [InlineData("toolbelt=:Whetstone")]
    public void BadNewFormsAreErrors(string text)
    {
        Assert.False(Change.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AListHoldsOneChangePerKey()
    {
        var list = ChangeList.Parse("star=A:on star=B:on guild=Attack:1").With(Change.Parse("guild=Attack:3"));
        Assert.Equal("guild=Attack:3 star=A:on star=B:on", list.ShortForm);
        Assert.Throws<FormatException>(() => ChangeList.Parse("shop=AutoEat2:1 shop=AutoEat2:2"));
    }

    [Fact]
    public void AListIsInCanonicalOrderWhateverOrderItWasMadeIn()
    {
        var text =
            "prayer=Thick_Skin:on potion=none food=Shrimp imbuement=none spell=AirBolt stance=Stab "
            + "toolbelt=CombatUtility1:none equip=Weapon:none star=A:on guild=Attack:2 shop=AutoEat2:1 "
            + "quest=MQ3:done level=Attack:40";
        Assert.Equal(
            "level=Attack:40 quest=MQ3:done shop=AutoEat2:1 guild=Attack:2 star=A:on equip=Weapon:none "
                + "toolbelt=CombatUtility1:none stance=Stab spell=AirBolt imbuement=none food=Shrimp potion=none "
                + "prayer=Thick_Skin:on",
            ChangeList.Parse(text).ShortForm
        );
    }

    [Theory]
    [InlineData(ChangeState.Accepted, "accepted", true, false)]
    [InlineData(ChangeState.NoChange, "no-change", false, false)]
    [InlineData(ChangeState.Refused, "refused", false, true)]
    [InlineData(ChangeState.RequirementIgnored, "requirement-ignored", true, false)]
    [InlineData(ChangeState.NotInBuild, "not-in-build", false, true)]
    public void EachStateHasANameAndAppliesOrIsLeftOut(ChangeState state, string name, bool applies, bool leftOut)
    {
        var outcome = new ChangeOutcome(Change.Parse("star=A:on"), null, state, []);
        Assert.Equal(name, ChangeStates.Name(state));
        Assert.Equal(applies, outcome.Applies);
        Assert.Equal(leftOut, outcome.LeftOut);
    }

    [Fact]
    public void EachChangeIsNamedInPlainWords()
    {
        var notes = ChangeNotes.Of(
            [
                new(Change.Parse("star=Terra_Minor_3:on"), "off", ChangeState.Accepted, []),
                new(Change.Parse("shop=Gone:1"), null, ChangeState.NotInBuild, []),
                new(Change.Parse("guild=Attack:2"), "0", ChangeState.Accepted, []),
                new(Change.Parse("quest=MQ3:done"), "open", ChangeState.Accepted, []),
                new(Change.Parse("toolbelt=CombatUtility1:none"), "MelvorBase:Whetstone", ChangeState.Accepted, []),
            ],
            id => id.Replace("MelvorBase:", "", StringComparison.Ordinal)
        );
        Assert.Equal(
            [
                "Star Terra_Minor_3 on",
                "left out: Gone: ? → 1 bought (not in this build)",
                "Attack guild rank 0 → 2",
                "Quest MQ3 done",
                "CombatUtility1: Whetstone → empty",
            ],
            notes.Select(n => n.English())
        );
    }
}
