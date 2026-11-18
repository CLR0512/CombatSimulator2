using MelvorMods.CombatSimulator2.Common;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RedactTests
{
    private static readonly RedactionNames Alexandra = new(@"C:\Users\Alexandra", "Alexandra", "ALEX-DESKTOP");

    [Theory]
    [InlineData(@"C:\Users\Alexandra\AppData\Roaming", @"<user>\AppData\Roaming")]
    [InlineData("C:/Users/Alexandra/AppData", "<user>/AppData")]
    [InlineData(@"c:\users\alexandra\Desktop", @"<user>\Desktop")]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\alexandra\wfn", @"\\wsl.localhost\Ubuntu\home\<user>\wfn")]
    [InlineData("/mnt/c/Users/ALEXANDRA/x", "/mnt/c/Users/<user>/x")]
    [InlineData("saved by Alexandra on ALEX-DESKTOP", "saved by <user> on <machine>")]
    [InlineData("alex-desktop and Alexandra's run", "<machine> and <user>'s run")]
    public void TheProfileUserAndMachineAreHidden(string text, string expected) =>
        Assert.Equal(expected, Redact.Text(text, Alexandra));

    [Fact]
    public void ANameInsideALongerWordStays() =>
        Assert.Equal("Alexandrayne AlexandraX", Redact.Text("Alexandrayne AlexandraX", Alexandra));

    [Fact]
    public void AUserNameThatIsACommonWordIsHiddenEvenInProse()
    {
        var names = new RedactionNames(@"C:\Users\Melvor", "Melvor", "PC1");
        Assert.Equal(
            @"<user> Idle 2 at <user>\Steam on <machine>",
            Redact.Text(@"Melvor Idle 2 at C:\Users\Melvor\Steam on PC1", names)
        );
    }

    [Fact]
    public void AUserCalledUserLeavesThePlaceholderAlone()
    {
        var names = new RedactionNames(@"C:\Users\user", "user", "box");
        Assert.Equal(
            @"<user>\AppData and \home\<user>\x, the <user>",
            Redact.Text(@"C:\Users\user\AppData and \home\user\x, the user", names)
        );
    }

    [Fact]
    public void AShortNameIsHiddenOnlyAsAPathSegment()
    {
        var names = new RedactionNames(null, "al", "pc");
        Assert.Equal(
            @"C:\Users\<user>\x al and pc at \\<machine>\share",
            Redact.Text(@"C:\Users\al\x al and pc at \\pc\share", names)
        );
    }

    [Fact]
    public void MissingNamesChangeNothing() =>
        Assert.Equal("text", Redact.Text("text", new RedactionNames(null, null, "")));
}
