using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SetupPageTests
{
    private static readonly ChangeList Changes = ChangeList.Parse(
        "level=Attack:40 equip=Weapon:none toolbelt=CombatUtility1:none stance=Stab spell=AirBolt imbuement=none "
            + "food=Shrimp potion=none prayer=Thick_Skin:on star=A:on star=B:on guild=Attack:2 shop=AutoEat2:1 "
            + "quest=MQ3:done pet=Bruno:on bonfire=out technique=Climbing:40"
    );

    [Theory]
    [InlineData(SetupPage.Gear, 8)]
    [InlineData(SetupPage.Herblore, 1)]
    [InlineData(SetupPage.Firemaking, 1)]
    [InlineData(SetupPage.Agility, 1)]
    [InlineData(SetupPage.Astrology, 2)]
    [InlineData(SetupPage.Guild, 1)]
    [InlineData(SetupPage.Shop, 1)]
    [InlineData(SetupPage.Quests, 1)]
    [InlineData(SetupPage.Slayer, 0)]
    [InlineData(SetupPage.Pets, 1)]
    public void AChangeCountsOnThePageItsKindBelongsTo(string page, int count) =>
        Assert.Equal(count, SetupPage.ChangeCount(page, Changes));

    [Fact]
    public void EveryChangeCountsOnExactlyOnePage() =>
        Assert.Equal(Changes.Count, SetupPage.All.Sum(p => SetupPage.ChangeCount(p.Id, Changes)));

    [Fact]
    public void EveryKindHasAPageThatIsListed()
    {
        var ids = SetupPage.All.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(10, ids.Count);
        Assert.All(Enum.GetValues<ChangeKind>(), kind => Assert.Contains(SetupPage.PageOf(kind), ids));
    }

    [Fact]
    public void APageWithGateTextsIsGated()
    {
        var open = new SetupPage(SetupPage.Astrology, PageSection.Skills, PageState.Changeable, null, [], []);
        Assert.False(open.Gated);
        Assert.True((open with { Gate = [Note.Text("Requires Astrology level 10")] }).Gated);
    }

    [Fact]
    public void ClearingAPageKeepsTheOtherPagesChanges()
    {
        var changes = ChangeList.Parse("level=Strength:50 shop=AutoEat2:1 quest=MQ8:done star=DeedreeA1:on");
        var kept = SetupPage.WithoutPage(SetupPage.Shop, changes);
        Assert.Equal(ChangeList.Parse("level=Strength:50 quest=MQ8:done star=DeedreeA1:on"), kept);
        Assert.Equal(
            ChangeList.Parse("shop=AutoEat2:1 quest=MQ8:done star=DeedreeA1:on"),
            SetupPage.WithoutPage(SetupPage.Gear, changes)
        );
        Assert.Same(changes, SetupPage.WithoutPage(SetupPage.Guild, changes));
    }
}
