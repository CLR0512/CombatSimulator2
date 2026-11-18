using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ChangeSlayerTaskTests
{
    [Theory]
    [InlineData("task=on", "on")]
    [InlineData("task=off", "off")]
    public void TheSidesChoiceHasNoSubject(string text, string value)
    {
        var change = Change.Parse(text);
        Assert.Equal(ChangeKind.SlayerTask, change.Kind);
        Assert.Null(change.Subject);
        Assert.Equal(value, change.Value);
        Assert.Equal(text, change.ShortForm);
    }

    [Theory]
    [InlineData("taskfor=SlimeForest/RedSlime:on", "MelvorBase:SlimeForest/MelvorBase:RedSlime", "on")]
    [InlineData("taskfor=SlimeForest/RedSlime:off", "MelvorBase:SlimeForest/MelvorBase:RedSlime", "off")]
    [InlineData("taskfor=SlimeForest/RedSlime:save", "MelvorBase:SlimeForest/MelvorBase:RedSlime", "save")]
    [InlineData("taskfor=Mod:Area/Mod:Beast:on", "Mod:Area/Mod:Beast", "on")]
    [InlineData("taskfor=SlimeForest/Mod:Beast:on", "MelvorBase:SlimeForest/Mod:Beast", "on")]
    public void AMonstersChoiceIsKeyedByItsTarget(string text, string subject, string value)
    {
        var change = Change.Parse(text);
        Assert.Equal(ChangeKind.SlayerTaskFor, change.Kind);
        Assert.Equal(subject, change.Subject);
        Assert.Equal(value, change.Value);
        Assert.Equal(text, change.ShortForm);
        Assert.Equal(change, Change.Parse(change.ShortForm));
    }

    [Fact]
    public void FullIdsReadAsShortOnes() =>
        Assert.Equal(
            "taskfor=SlimeForest/RedSlime:on",
            Change.Parse("taskfor=MelvorBase:SlimeForest/MelvorBase:RedSlime:on").ShortForm
        );

    [Theory]
    [InlineData("task=save")]
    [InlineData("task=")]
    [InlineData("task=Kuren:on")]
    [InlineData("taskfor=RedSlime:on")]
    [InlineData("taskfor=SlimeForest/RedSlime")]
    [InlineData("taskfor=SlimeForest/RedSlime:maybe")]
    [InlineData("taskfor=/RedSlime:on")]
    [InlineData("taskfor=A/B/C:on")]
    public void OtherFormsAreRefused(string text)
    {
        Assert.False(Change.TryParse(text, out _, out var why));
        Assert.False(string.IsNullOrEmpty(why));
    }

    private const string RedSlime = "MelvorBase:SlimeForest/MelvorBase:RedSlime";
    private const string GreenSlime = "MelvorBase:SlimeForest/MelvorBase:GreenSlime";

    [Theory]
    [InlineData("", null, null)]
    [InlineData("blocks=Kuren:Golbin", null, null)]
    [InlineData("task=on", "on", "on")]
    [InlineData("task=off", "off", "off")]
    [InlineData("task=off taskfor=SlimeForest/RedSlime:on", "on", "off")]
    [InlineData("task=on taskfor=SlimeForest/RedSlime:save", null, "on")]
    [InlineData("taskfor=SlimeForest/RedSlime:off", "off", null)]
    public void AMonstersOwnChoiceWinsOverTheSides(string changes, string? red, string? green)
    {
        var list = ChangeList.Parse(changes);
        Assert.Equal(red, Change.TaskChoice(list, RedSlime));
        Assert.Equal(green, Change.TaskChoice(list, GreenSlime));
    }

    [Fact]
    public void NoChangesIsAsTheSave() => Assert.Null(Change.TaskChoice(null, RedSlime));

    [Fact]
    public void AListHoldsOneChoicePerMonsterAndOneForTheSide()
    {
        var list = ChangeList
            .Parse("taskfor=SlimeForest/RedSlime:on task=off blocks=Kuren:Golbin")
            .With(Change.Parse("taskfor=SlimeForest/RedSlime:save"))
            .With(Change.Parse("task=on"))
            .With(Change.Parse("taskfor=SlimeForest/GreenSlime:off"));
        Assert.Equal(
            "blocks=Kuren:Golbin task=on taskfor=SlimeForest/GreenSlime:off taskfor=SlimeForest/RedSlime:save",
            list.ShortForm
        );
    }
}
