using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class FailureRecordTests
{
    [Fact]
    public void EveryKindHasItsOwnName()
    {
        var names = Enum.GetValues<FailureKind>().Select(FailureRecord.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("copy", FailureRecord.Name(FailureKind.Copy));
        Assert.Equal("load", FailureRecord.Name(FailureKind.Load));
    }

    [Fact]
    public void ADroppedCopyHasNoBanner()
    {
        static FailureRecord Of(FailureKind kind) => new() { Kind = kind, Reason = Note.Of("x") };

        Assert.False(Of(FailureKind.Copy).HasBanner);
        Assert.True(Of(FailureKind.Target).HasBanner);
        Assert.True(Of(FailureKind.HostCrash).HasBanner);
    }
}
